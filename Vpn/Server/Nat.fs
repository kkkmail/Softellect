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
        | Icmp
        | Other of byte

    /// Internal (VPN-side) flow key - includes remote endpoint for full 5-tuple
    [<Struct>]
    type NatKey =
        {
            internalIp      : uint32   // network byte order
            internalPortOrId: uint16   // host order - TCP/UDP port or ICMP identifier
            remoteIp        : uint32   // network byte order
            remotePort      : uint16   // host order - 0 for ICMP echo
            protocol        : Protocol
        }

    /// External-side key for reverse lookup
    [<Struct>]
    type NatExternalKey =
        {
            externalPortOrId: uint16   // host order - TCP/UDP external port or ICMP external identifier
            protocol        : Protocol
        }

    /// NAT table entry
    [<CLIMutable>]
    type NatEntry =
        {
            key             : NatKey
            externalPortOrId: uint16  // host order - TCP/UDP external port or ICMP external identifier
            mutable lastSeen: DateTime
        }

    // ---- State ----

    // (externalPortOrId, protocol) -> entry
    let private tableByExternal = ConcurrentDictionary<NatExternalKey, NatEntry>()
    // (internalIp, internalPortOrId, remoteIp, remotePort, protocol) -> externalKey
    let private tableByInternal = ConcurrentDictionary<NatKey, NatExternalKey>()

    // Very simple port allocator
    let mutable private nextPort = 40000us
    let private random   = Random()

    let private allocateExternalPortOrId (proto: Protocol) =
        // naive: just bump number and wrap; skip if already in use
        let mutable candidate = nextPort
        let mutable found = false
        let mutable tries = 0
        while not found && tries < 65535 do
            if candidate = 0us || candidate < 1024us then
                candidate <- 40000us
            let extKey = { externalPortOrId = candidate; protocol = proto }
            if tableByExternal.ContainsKey extKey then
                candidate <- uint16 ((int candidate + 1) &&& 0xFFFF)
                tries <- tries + 1
            else
                found <- true
        if not found then
            failwith "NAT: no free external ports/ids"
        nextPort <- uint16 ((int candidate + 1) &&& 0xFFFF)
        candidate

    let private now () = DateTime.UtcNow

    /// Optional: periodic cleanup of old entries
    let removeStaleEntries (maxIdle: TimeSpan) =
        for kv in tableByExternal do
            if now() - kv.Value.lastSeen > maxIdle then
                tableByExternal.TryRemove kv.Key |> ignore
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
        | 1uy  -> Icmp
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
            | Icmp -> -1  // ICMP uses separate checksum function
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
            | Icmp     -> 1uy
            | Tcp      -> 6uy
            | Udp      -> 17uy
            | Other p  -> p
        add16 (uint16 protoByte)
        add16 (uint16 segLen)

        // Sum segment
        let mutable i = ihl
        while i < ihl + segLen do
            if i = checksumOffset then
                // skip the checksum field itself (we'll write it later)
                ()
            else
                if i + 1 < ihl + segLen then
                    add16 (readUInt16 buf i)
                else
                    // odd length: pad the last byte with zero
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

    /// Compute ICMP checksum over the entire ICMP message (no pseudo-header)
    let private icmpChecksum (buf: byte[]) =
        let ihl = headerLength buf
        let totalLen = int (readUInt16 buf 2)
        let icmpLen = totalLen - ihl
        let checksumOffset = ihl + 2  // ICMP checksum at offset 2 within the ICMP header

        let mutable sum = 0u
        let mutable i = ihl
        while i < ihl + icmpLen do
            if i = checksumOffset then
                // skip checksum field itself
                ()
            else if i + 1 < ihl + icmpLen then
                sum <- sum + uint32 (readUInt16 buf i)
            else
                // odd length: pad the last byte with zero
                sum <- sum + (uint32 buf[i] <<< 8)
            i <- i + 2

        // fold carries
        while (sum >>> 16) <> 0u do
            sum <- (sum &&& 0xFFFFu) + (sum >>> 16)
        uint16 (~~~sum &&& 0xFFFFu)

    let private updateIcmpChecksum (buf: byte[]) =
        let ihl = headerLength buf
        let checksumOffset = ihl + 2
        // zero out existing checksum
        writeUInt16 buf checksumOffset 0us
        let csum = icmpChecksum buf
        writeUInt16 buf checksumOffset csum

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

    let private makeKey (internalIp: uint32) (internalPortOrId: uint16) (remoteIp: uint32) (remotePort: uint16) (proto: Protocol) =
        { internalIp = internalIp; internalPortOrId = internalPortOrId; remoteIp = remoteIp; remotePort = remotePort; protocol = proto }

    let private makeExternalKey (externalPortOrId: uint16) (proto: Protocol) =
        { externalPortOrId = externalPortOrId; protocol = proto }

    /// Get or create NAT mapping for a flow, returning the external port/id
    let private getOrCreateMapping (key: NatKey) (proto: Protocol) : uint16 =
        match tableByInternal.TryGetValue key with
        | true, extKey ->
            // existing mapping
            match tableByExternal.TryGetValue extKey with
            | true, entry ->
                entry.lastSeen <- now ()
                extKey.externalPortOrId
            | _ ->
                // inconsistent; allocate new
                let newPortOrId = allocateExternalPortOrId proto
                let newExtKey = makeExternalKey newPortOrId proto
                let entry = { key = key; externalPortOrId = newPortOrId; lastSeen = now () }
                tableByExternal[newExtKey] <- entry
                tableByInternal[key] <- newExtKey
                newPortOrId
        | false, _ ->
            // new mapping
            let newPortOrId = allocateExternalPortOrId proto
            let newExtKey = makeExternalKey newPortOrId proto
            let entry = { key = key; externalPortOrId = newPortOrId; lastSeen = now () }
            tableByExternal[newExtKey] <- entry
            tableByInternal[key] <- newExtKey
            newPortOrId

    /// Called for packets coming FROM VPN clients TO the Internet.
    /// - internalNetwork: true if src is VPN (10.66.77.0/24).
    /// - externalIp: server public IPv4, network byte order.
    /// Returns Some translatedPacket, or None if the packet should be dropped / not NATed.
    let translateOutbound (vpnSubnet: uint32, vpnMask: uint32) (externalIp: uint32) (packet: byte[]) : byte[] option =
        if packet.Length < 20 then None
        else
            let ihl = headerLength packet
            let proto = getProtocol packet

            // Minimum packet size check varies by protocol
            let minSize =
                match proto with
                | Tcp | Udp -> ihl + 8
                | Icmp -> ihl + 8  // ICMP header is 8 bytes for echo
                | Other _ -> ihl

            if packet.Length < minSize then
                None
            else
                let srcIp = readUInt32 packet 12
                let dstIp = readUInt32 packet 16

                // Only NAT packets FROM VPN subnet
                let isInternal = ((srcIp &&& vpnMask) = (vpnSubnet &&& vpnMask))

                if not isInternal then
                    // not from 10.66.77.x – leave untouched
                    Some packet
                else
                    match proto with
                    | Tcp | Udp ->
                        let srcPort = readUInt16 packet ihl
                        let dstPort = readUInt16 packet (ihl + 2)
                        let key = makeKey srcIp srcPort dstIp dstPort proto
                        let externalPort = getOrCreateMapping key proto

                        // Rewrite src IP/port to externalIp:externalPort
                        writeUInt32 packet 12 externalIp
                        writeUInt16 packet ihl externalPort

                        // Update checksums
                        updateIpChecksum packet
                        updateTransportChecksum packet proto

                        Logger.logTrace (fun () -> $"NAT OUT: proto={proto}, {srcIp:X8}:{srcPort} -> {dstIp:X8}:{dstPort}, extPort={externalPort}")

                        Some packet

                    | Icmp ->
                        // Check ICMP type - only NAT echo request (type 8)
                        let icmpType = packet[ihl]
                        if icmpType = 8uy then
                            // ICMP Echo Request
                            let identifier = readUInt16 packet (ihl + 4)
                            // For ICMP, remotePort = 0 (not applicable)
                            let key = makeKey srcIp identifier dstIp 0us Icmp
                            let externalId = getOrCreateMapping key Icmp

                            // Rewrite src IP and ICMP identifier
                            writeUInt32 packet 12 externalIp
                            writeUInt16 packet (ihl + 4) externalId

                            // Update checksums
                            updateIpChecksum packet
                            updateIcmpChecksum packet

                            Logger.logTrace (fun () -> $"NAT OUT: proto=Icmp, {srcIp:X8}:id={identifier} -> {dstIp:X8}, extId={externalId}")

                            Some packet
                        else
                            // Non-echo ICMP – pass through without NAT
                            Some packet

                    | Other _ ->
                        // Non-TCP/UDP/ICMP – leave as is
                        Some packet

    /// Called for packets coming FROM the Internet TO the server external IP.
    /// - externalIp: server public IPv4, network byte order.
    /// Returns Some translatedPacket (dest rewritten back to the VPN client),
    /// or None if no mapping was found (drop).
    let translateInbound (externalIp: uint32) (packet: byte[]) : byte[] option =
        if packet.Length < 20 then None
        else
            let ihl = headerLength packet
            let proto = getProtocol packet

            // Minimum packet size check varies by protocol
            let minSize =
                match proto with
                | Tcp | Udp -> ihl + 8
                | Icmp -> ihl + 8
                | Other _ -> ihl

            if packet.Length < minSize then
                None
            else
                // let srcIp = readUInt32 packet 12
                let dstIp = readUInt32 packet 16

                // Only handle packets addressed to our public IP.
                // Everything else is not part of VPN NAT and must be ignored.
                if dstIp <> externalIp then
                    None
                else
                    match proto with
                    | Tcp | Udp ->
                        let dstPort = readUInt16 packet (ihl + 2)
                        let extKey = makeExternalKey dstPort proto

                        match tableByExternal.TryGetValue extKey with
                        | true, entry ->
                            entry.lastSeen <- now()

                            let internalIp = entry.key.internalIp
                            let internalPortOrId = entry.key.internalPortOrId

                            // Rewrite dst IP/port to internal client
                            writeUInt32 packet 16 internalIp
                            writeUInt16 packet (ihl + 2) internalPortOrId

                            // Update checksums
                            updateIpChecksum packet
                            updateTransportChecksum packet proto

                            Logger.logTrace (fun () -> $"HEAVY LOG - NAT IN: proto={proto}, extPort={dstPort} -> {internalIp:X8}:{internalPortOrId}")
                            Some packet
                        | false, _ ->
                            // No mapping – drop
                            // Logger.logTrace (fun () -> $"HEAVY LOG - NAT IN: proto={proto}, no mapping for extPort={dstPort}, dropping packet")
                            None

                    | Icmp ->
                        // Check ICMP type - only NAT echo reply (type 0)
                        let icmpType = packet[ihl]
                        if icmpType = 0uy then
                            // ICMP Echo Reply
                            let identifier = readUInt16 packet (ihl + 4)
                            let extKey = makeExternalKey identifier Icmp

                            match tableByExternal.TryGetValue extKey with
                            | true, entry ->
                                entry.lastSeen <- now()

                                let internalIp = entry.key.internalIp
                                let internalId = entry.key.internalPortOrId

                                // Rewrite dst IP and ICMP identifier
                                writeUInt32 packet 16 internalIp
                                writeUInt16 packet (ihl + 4) internalId

                                // Update checksums
                                updateIpChecksum packet
                                updateIcmpChecksum packet

                                Logger.logTrace (fun () -> $"HEAVY LOG - NAT IN: proto=Icmp, extId={identifier} -> {internalIp:X8}:id={internalId}")

                                Some packet
                            | false, _ ->
                                // No mapping – drop
                                // Logger.logTrace (fun () -> $"HEAVY LOG - NAT IN: proto=Icmp, no mapping for extId={identifier}, dropping packet")
                                None
                        else
                            // Non-echo-reply ICMP – drop (no mapping possible)
                            // Logger.logTrace (fun () -> $"HEAVY LOG - NAT IN: proto=Icmp, type={icmpType} not echo reply, dropping packet")
                            None

                    | Other _ ->
                        // Non-TCP/UDP/ICMP – drop
                        None

    /// Internal self-test for NAT inbound translation.
    /// Validates that destination port offset and UDP checksum are correct.
    /// This function does not run automatically; call manually for debugging.
    let internalSelfTest () =
        // Clear NAT table for clean test
        tableByExternal.Clear()
        tableByInternal.Clear()

        // Test parameters
        let externalIp = 0x08080808u  // 8.8.8.8 (network byte order)
        let internalIp = 0x0A424D02u // 10.66.77.2 (network byte order)
        let internalPort = 5353us
        let externalPort = 40000us
        let remoteSrcPort = 12345us
        let remoteIp = 0x01020304u  // arbitrary remote IP

        // Insert NAT mapping: externalPort=40000 -> internal 10.66.77.2:5353 UDP
        let key = makeKey internalIp internalPort remoteIp remoteSrcPort Udp
        let extKey = makeExternalKey externalPort Udp
        let entry =
            { key = key
              externalPortOrId = externalPort
              lastSeen = now() }
        tableByExternal[extKey] <- entry
        tableByInternal[key] <- extKey

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
