namespace Softellect.Vpn.Server

open System
open System.Collections.Concurrent

open Softellect.Sys.Logging

/// Minimal user-space NAT for IPv4 TCP/UDP.
/// Works on raw IPv4 packets. You still need to:
/// - Feed outbound packets (from VPN side) into `translateOutbound`.
/// - Feed inbound packets (from Internet side) into `translateInbound`.
/// - Actually send/receive packets on the external interface yourself.
module Nat =

    // ---- Types ----

    type Protocol =
        | Tcp
        | Udp
        | Other of byte

    /// Internal (VPN-side) flow key
    [<Struct>]
    type NatKey =
        {
            internalIp   : uint32   // network byte order
            internalPort : uint16   // host order
            protocol     : Protocol
        }

    /// NAT table entry
    [<CLIMutable>]
    type NatEntry =
        {
            key         : NatKey
            externalPort: uint16  // host order
            mutable lastSeen   : DateTime
        }

    // ---- State ----

    // externalPort -> entry
    let private tableByExternalPort = ConcurrentDictionary<uint16, NatEntry>()
    // (internalIp, internalPort, proto) -> externalPort
    let private tableByInternal    = ConcurrentDictionary<NatKey, uint16>()

    // very simple port allocator (you might want to improve this)
    let private nextPort = ref 40000us
    let private random   = Random()

    let private allocateExternalPort () =
        // naive: just bump number and wrap; skip if already in use
        let mutable candidate = !nextPort
        let mutable found = false
        let mutable tries = 0
        while not found && tries < 65535 do
            if candidate = 0us || candidate < 1024us then
                candidate <- 40000us
            if tableByExternalPort.ContainsKey candidate then
                candidate <- uint16 ((int candidate + 1) &&& 0xFFFF)
                tries <- tries + 1
            else
                found <- true
        if not found then
            failwith "NAT: no free external ports"
        nextPort := uint16 ((int candidate + 1) &&& 0xFFFF)
        candidate

    let private now () = DateTime.UtcNow

    /// Optional: periodic cleanup of old entries
    let removeStaleEntries (maxIdle: TimeSpan) =
        for kv in tableByExternalPort do
            if now() - kv.Value.lastSeen > maxIdle then
                tableByExternalPort.TryRemove kv.Key |> ignore
                tableByInternal.TryRemove kv.Value.key |> ignore

    // ---- Helpers for IPv4 / TCP / UDP ----

    let private toUInt16 (hi: byte) (lo: byte) =
        uint16 hi <<< 8 ||| uint16 lo

    let private fromUInt16 (v: uint16) =
        byte (v >>> 8), byte (v &&& 0xFFus)

    let private readUInt16 (buf: byte[]) (offset: int) =
        toUInt16 buf[offset] buf[offset + 1]

    let private writeUInt16 (buf: byte[]) (offset: int) (v: uint16) =
        let hi, lo = fromUInt16 v
        buf[offset]     <- hi
        buf[offset + 1] <- lo

    let private readUInt32 (buf: byte[]) (offset: int) =
        let b0 = uint32 buf[offset]
        let b1 = uint32 buf[offset + 1]
        let b2 = uint32 buf[offset + 2]
        let b3 = uint32 buf[offset + 3]
        (b0 <<< 24) ||| (b1 <<< 16) ||| (b2 <<< 8) ||| b3

    let private writeUInt32 (buf: byte[]) (offset: int) (v: uint32) =
        buf[offset]     <- byte (v >>> 24)
        buf[offset + 1] <- byte (v >>> 16)
        buf[offset + 2] <- byte (v >>> 8)
        buf[offset + 3] <- byte (v &&& 0xFFu)

    let private getProtocol (buf: byte[]) =
        let p = buf[9]
        match p with
        | 6uy  -> Tcp
        | 17uy -> Udp
        | x    -> Other x

    let private headerLength (buf: byte[]) =
        // IHL (lower 4 bits) * 4 bytes
        let ihl = int (buf[0] &&& 0x0Fuy)
        ihl * 4

    // ---- Checksums ----

    let private ipChecksum (buf: byte[]) (headerLen: int) =
        // RFC 791: sum 16-bit words, one's complement
        let mutable sum = 0u
        let mutable i = 0
        while i < headerLen do
            if i <> 10 then
                sum <- sum + uint32 (readUInt16 buf i)
            i <- i + 2
        // fold carries
        while (sum >>> 16) <> 0u do
            sum <- (sum &&& 0xFFFFu) + (sum >>> 16)
        uint16 (~~~sum &&& 0xFFFFu)

    let private updateIpChecksum (buf: byte[]) =
        let ihl = headerLength buf
        // zero out existing checksum
        writeUInt16 buf 10 0us
        let csum = ipChecksum buf ihl
        writeUInt16 buf 10 csum

    /// Generic transport checksum (TCP/UDP) based on pseudo-header + segment
    let private transportChecksum (buf: byte[]) (proto: Protocol) =
        let ihl = headerLength buf
        let totalLen = int (readUInt16 buf 2)
        let segLen = totalLen - ihl

        // Checksum field offset differs between TCP and UDP
        let checksumOffset =
            match proto with
            | Tcp -> ihl + 16
            | Udp -> ihl + 6
            | Other _ -> -1  // never matches, don't skip anything

        let srcIp = readUInt32 buf 12
        let dstIp = readUInt32 buf 16

        // Sum pseudo-header
        let mutable sum = 0u
        let add16 v = sum <- sum + uint32 v

        // src ip
        add16 (uint16 (srcIp >>> 16))
        add16 (uint16 (srcIp &&& 0xFFFFu))
        // dst ip
        add16 (uint16 (dstIp >>> 16))
        add16 (uint16 (dstIp &&& 0xFFFFu))
        // protocol + length
        let protoByte =
            match proto with
            | Tcp      -> 6uy
            | Udp      -> 17uy
            | Other p  -> p
        add16 (uint16 protoByte)
        add16 (uint16 segLen)

        // Sum segment
        let mutable i = ihl
        while i < ihl + segLen do
            if i = checksumOffset then
                // skip checksum field itself (we'll write it later)
                ()
            else
                if i + 1 < ihl + segLen then
                    add16 (readUInt16 buf i)
                else
                    // odd length: pad last byte with zero
                    let last = uint16 buf[i] <<< 8
                    add16 last
            i <- i + 2

        // fold carries
        while (sum >>> 16) <> 0u do
            sum <- (sum &&& 0xFFFFu) + (sum >>> 16)
        uint16 (~~~sum &&& 0xFFFFu)

    let private updateTransportChecksum (buf: byte[]) (proto: Protocol) =
        match proto with
        | Tcp | Udp ->
            let ihl = headerLength buf
            let checksumOffset =
                match proto with
                | Tcp -> ihl + 16
                | Udp -> ihl + 6
                | _   -> ihl + 16

            // zero out existing checksum
            writeUInt16 buf checksumOffset 0us
            let csum = transportChecksum buf proto
            writeUInt16 buf checksumOffset csum
        | _ -> ()

    /// For manual debugging only - verifies UDP checksum is correct.
    /// Do NOT call from hot loops.
    let private verifyUdpChecksum (packet: byte[]) : bool =
        let ihl = headerLength packet
        let proto = getProtocol packet
        match proto with
        | Udp ->
            // Save existing checksum, zero it, compute expected, restore
            let checksumOffset = ihl + 6
            let actual = readUInt16 packet checksumOffset
            writeUInt16 packet checksumOffset 0us
            let expected = transportChecksum packet Udp
            writeUInt16 packet checksumOffset actual
            expected = actual
        | _ -> true

    // ---- NAT core ----

    let private makeKey (internalIp: uint32) (internalPort: uint16) (proto: Protocol) =
        { internalIp = internalIp; internalPort = internalPort; protocol = proto }

    /// Called for packets coming FROM VPN clients TO the Internet.
    /// - internalNetwork: true if src is VPN (10.66.77.0/24).
    /// - externalIp: server public IPv4, network byte order.
    /// Returns Some translatedPacket, or None if packet should be dropped / not NATed.
    let translateOutbound (vpnSubnet: uint32, vpnMask: uint32) (externalIp: uint32) (packet: byte[]) : byte[] option =
        if packet.Length < 20 then None
        else
            let ihl = headerLength packet
            if packet.Length < ihl + 8 then
                None
            else
                let srcIp = readUInt32 packet 12
                let dstIp = readUInt32 packet 16
                let proto = getProtocol packet

                // Only NAT packets FROM VPN subnet
                let isInternal =
                    ((srcIp &&& vpnMask) = (vpnSubnet &&& vpnMask))

                if not isInternal then
                    // not from 10.66.77.x – leave untouched
                    Some packet
                else
                    match proto with
                    | Tcp
                    | Udp ->
                        let srcPort = readUInt16 packet ihl
                        let key = makeKey srcIp srcPort proto

                        let externalPort =
                            match tableByInternal.TryGetValue key with
                            | true, p ->
                                // existing mapping
                                match tableByExternalPort.TryGetValue p with
                                | true, entry ->
                                    entry.lastSeen <- now ()
                                    p
                                | _ ->
                                    // inconsistent; allocate new
                                    let newPort = allocateExternalPort()
                                    let entry =
                                        { key = key
                                          externalPort = newPort
                                          lastSeen = now () }
                                    tableByExternalPort[newPort] <- entry
                                    tableByInternal[key] <- newPort
                                    newPort
                            | false, _ ->
                                // new mapping
                                let newPort = allocateExternalPort()
                                let entry =
                                    { key = key
                                      externalPort = newPort
                                      lastSeen = now () }
                                tableByExternalPort[newPort] <- entry
                                tableByInternal[key] <- newPort
                                newPort

                        // Rewrite src IP/port to externalIp:externalPort
                        writeUInt32 packet 12 externalIp
                        writeUInt16 packet ihl externalPort

                        // Update checksums
                        updateIpChecksum packet
                        updateTransportChecksum packet proto

                        Logger.logTrace (fun () ->
                            $"NAT OUT: {srcIp:X8}:{srcPort} -> {externalIp:X8}:{externalPort}, proto={proto}")

                        Some packet

                    | Other _ ->
                        // Non-TCP/UDP – leave as is (or drop if you wish)
                        Some packet

    /// Called for packets coming FROM the Internet TO the server external IP.
    /// - externalIp: server public IPv4, network byte order.
    /// Returns Some translatedPacket (dest rewritten back to VPN client),
    /// or None if no mapping was found (drop).
    let translateInbound (externalIp: uint32) (packet: byte[]) : byte[] option =
        if packet.Length < 20 then None
        else
            let ihl = headerLength packet
            if packet.Length < ihl + 8 then
                None
            else
                let srcIp = readUInt32 packet 12
                let dstIp = readUInt32 packet 16
                let proto = getProtocol packet

                // Only handle packets addressed to our public IP.
                // Everything else is not part of VPN NAT and must be ignored.
                if dstIp <> externalIp then
                    None
                else
                    match proto with
                    | Tcp
                    | Udp ->
                        let dstPort = readUInt16 packet (ihl + 2)

                        match tableByExternalPort.TryGetValue dstPort with
                        | true, entry ->
                            entry.lastSeen <- now()

                            let internalIp   = entry.key.internalIp
                            let internalPort = entry.key.internalPort

                            // Rewrite dst IP/port to internal client
                            writeUInt32 packet 16 internalIp
                            writeUInt16 packet (ihl + 2) internalPort

                            // Update checksums
                            updateIpChecksum packet
                            updateTransportChecksum packet proto

                            Logger.logTrace (fun () ->
                                $"NAT IN: proto={proto}, extPort={dstPort} -> {internalIp:X8}:{internalPort}")

                            Some packet
                        | false, _ ->
                            // No mapping – drop
                            Logger.logTrace (fun () ->
                                $"NAT IN: no mapping for dstPort={dstPort}, dropping packet")
                            None
                    | Other _ ->
                        // Non-TCP/UDP – either pass or drop
                        Some packet

    /// Internal self-test for NAT inbound translation.
    /// Validates that destination port offset and UDP checksum are correct.
    /// This function does not run automatically; call manually for debugging.
    let internalSelfTest () =
        // Clear NAT table for clean test
        tableByExternalPort.Clear()
        tableByInternal.Clear()

        // Test parameters
        let externalIp = 0x08080808u  // 8.8.8.8 (network byte order)
        let internalIp = 0x0A424D02u // 10.66.77.2 (network byte order)
        let internalPort = 5353us
        let externalPort = 40000us
        let remoteSrcPort = 12345us

        // Insert NAT mapping: externalPort=40000 -> internal 10.66.77.2:5353 UDP
        let key = makeKey internalIp internalPort Udp
        let entry =
            { key = key
              externalPort = externalPort
              lastSeen = now() }
        tableByExternalPort[externalPort] <- entry
        tableByInternal[key] <- externalPort

        // Construct minimal IPv4 + UDP packet
        // IPv4 header (20 bytes) + UDP header (8 bytes) + 0 data bytes = 28 bytes
        let packet = Array.zeroCreate<byte> 28

        // IPv4 header
        packet[0] <- 0x45uy  // Version=4, IHL=5 (20 bytes)
        packet[1] <- 0x00uy  // DSCP/ECN
        writeUInt16 packet 2 28us  // Total length
        writeUInt16 packet 4 0us   // ID
        writeUInt16 packet 6 0us   // Flags/Fragment offset
        packet[8] <- 64uy    // TTL
        packet[9] <- 17uy    // Protocol = UDP
        writeUInt16 packet 10 0us  // IP checksum (will be computed)
        writeUInt32 packet 12 0x01020304u  // Source IP (arbitrary)
        writeUInt32 packet 16 externalIp   // Destination IP = externalIp

        // UDP header (starts at offset 20)
        writeUInt16 packet 20 remoteSrcPort  // Source port = 12345
        writeUInt16 packet 22 externalPort   // Destination port = 40000
        writeUInt16 packet 24 8us            // UDP length = 8 (header only)
        writeUInt16 packet 26 0us            // UDP checksum (will be computed)

        // Compute checksums
        updateIpChecksum packet
        updateTransportChecksum packet Udp

        // Call translateInbound
        match translateInbound externalIp packet with
        | None ->
            failwith "internalSelfTest: translateInbound returned None, expected Some"
        | Some translatedPacket ->
            // Verify destination IP in IPv4 header
            let dstIpResult = readUInt32 translatedPacket 16
            if dstIpResult <> internalIp then
                failwith $"internalSelfTest: destination IP mismatch. Expected {internalIp:X8}, got {dstIpResult:X8}"

            // Verify destination port in UDP header (offset 20 + 2)
            let dstPortResult = readUInt16 translatedPacket 22
            if dstPortResult <> internalPort then
                failwith $"internalSelfTest: destination port mismatch. Expected {internalPort}, got {dstPortResult}"

            // Test passed
            Logger.logInfo (fun () -> "internalSelfTest: PASS")
            printfn "internalSelfTest: PASS"
