namespace Softellect.Vpn.Server

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Sockets

open Softellect.Sys.Logging

/// ICMP proxy for VPN gateway.
/// Intercepts ICMP Echo Requests to external addresses, forwards them, and routes replies back to clients.
module IcmpProxy =

    // ---- Types ----

    /// Key for tracking outstanding ICMP Echo Requests
    [<Struct>]
    type IcmpKey =
        {
            identifier : uint16
            sequence   : uint16
        }

    /// Entry storing client information for an outstanding request
    type IcmpEntry =
        {
            clientIp  : uint32  // network byte order
            timestamp : DateTime
        }

    // ---- State ----

    // (identifier, sequence) -> entry
    let private pendingRequests = ConcurrentDictionary<IcmpKey, IcmpEntry>()

    // ---- IPv4/ICMP helpers ----

    let private readUInt16 (buf: byte[]) (offset: int) =
        (uint16 buf[offset] <<< 8) ||| uint16 buf[offset + 1]

    let private writeUInt16 (buf: byte[]) (offset: int) (v: uint16) =
        buf[offset] <- byte (v >>> 8)
        buf[offset + 1] <- byte (v &&& 0xFFus)

    let private readUInt32 (buf: byte[]) (offset: int) =
        (uint32 buf[offset] <<< 24) |||
        (uint32 buf[offset + 1] <<< 16) |||
        (uint32 buf[offset + 2] <<< 8) |||
        uint32 buf[offset + 3]

    let private writeUInt32 (buf: byte[]) (offset: int) (v: uint32) =
        buf[offset] <- byte (v >>> 24)
        buf[offset + 1] <- byte (v >>> 16)
        buf[offset + 2] <- byte (v >>> 8)
        buf[offset + 3] <- byte v

    let private getIpHeaderLength (packet: byte[]) =
        let ihl = int (packet[0] &&& 0x0Fuy)
        ihl * 4

    let private getProtocol (packet: byte[]) =
        packet[9]

    /// Compute IPv4 header checksum
    let private computeIpChecksum (buf: byte[]) (headerLen: int) =
        let mutable sum = 0u
        for i in 0 .. 2 .. (headerLen - 1) do
            if i <> 10 then // skip checksum field
                sum <- sum + uint32 (readUInt16 buf i)
        // fold carries
        while (sum >>> 16) <> 0u do
            sum <- (sum &&& 0xFFFFu) + (sum >>> 16)
        uint16 (~~~sum &&& 0xFFFFu)

    /// Update IPv4 header checksum in place
    let private updateIpChecksum (buf: byte[]) =
        let headerLen = getIpHeaderLength buf
        writeUInt16 buf 10 0us
        let csum = computeIpChecksum buf headerLen
        writeUInt16 buf 10 csum

    /// Compute ICMP checksum over the entire ICMP message
    let private computeIcmpChecksum (buf: byte[]) =
        let ihl = getIpHeaderLength buf
        let totalLen = int (readUInt16 buf 2)
        let icmpLen = totalLen - ihl
        let checksumOffset = ihl + 2

        let mutable sum = 0u
        let mutable i = ihl
        while i < ihl + icmpLen do
            if i = checksumOffset then
                () // skip checksum field
            else if i + 1 < ihl + icmpLen then
                sum <- sum + uint32 (readUInt16 buf i)
            else
                // odd length: pad last byte with zero
                sum <- sum + (uint32 buf[i] <<< 8)
            i <- i + 2

        // fold carries
        while (sum >>> 16) <> 0u do
            sum <- (sum &&& 0xFFFFu) + (sum >>> 16)
        uint16 (~~~sum &&& 0xFFFFu)

    /// Update ICMP checksum in place
    let private updateIcmpChecksum (buf: byte[]) =
        let ihl = getIpHeaderLength buf
        let checksumOffset = ihl + 2
        writeUInt16 buf checksumOffset 0us
        let csum = computeIcmpChecksum buf
        writeUInt16 buf checksumOffset csum

    let private ipToString (ip: uint32) =
        $"{byte (ip >>> 24)}.{byte (ip >>> 16)}.{byte (ip >>> 8)}.{byte ip}"

    // ---- ICMP Proxy Logic ----

    /// Check if this packet is an ICMP Echo Request to an external address.
    /// Returns Some (srcIp, identifier, sequence) if it is, None otherwise.
    let tryParseIcmpEchoRequest (vpnSubnetUint: uint32) (vpnMaskUint: uint32) (packet: byte[]) : (uint32 * uint16 * uint16) option =
        // Min: 20 IP + 8 ICMP (type, code, checksum, identifier, sequence)
        if packet.Length < 28 then None
        else
            let version = int packet[0] >>> 4
            if version <> 4 then None
            else
                let ihl = getIpHeaderLength packet
                if packet.Length < ihl + 8 then None
                else
                    let protocol = getProtocol packet
                    if protocol <> 1uy then None // Not ICMP
                    else
                        let icmpType = packet[ihl]
                        if icmpType <> 8uy then None // Not Echo Request
                        else
                            let dstIp = readUInt32 packet 16
                            // Check if destination is outside VPN subnet
                            let isInsideVpn = (dstIp &&& vpnMaskUint) = (vpnSubnetUint &&& vpnMaskUint)
                            if isInsideVpn then None // Inside VPN, not for proxy
                            else
                                let srcIp = readUInt32 packet 12
                                let identifier = readUInt16 packet (ihl + 4)
                                let sequence = readUInt16 packet (ihl + 6)
                                Some (srcIp, identifier, sequence)

    /// Check if this packet is an ICMP Echo Reply.
    /// Returns Some (identifier, sequence) if it is, None otherwise.
    let tryParseIcmpEchoReply (packet: byte[]) : (uint16 * uint16) option =
        if packet.Length < 28 then None
        else
            let version = int packet[0] >>> 4
            if version <> 4 then None
            else
                let ihl = getIpHeaderLength packet
                if packet.Length < ihl + 8 then None
                else
                    let protocol = getProtocol packet
                    if protocol <> 1uy then None // Not ICMP
                    else
                        let icmpType = packet[ihl]
                        if icmpType <> 0uy then None // Not Echo Reply
                        else
                            let identifier = readUInt16 packet (ihl + 4)
                            let sequence = readUInt16 packet (ihl + 6)
                            Some (identifier, sequence)

    /// Handle outbound ICMP Echo Request from a VPN client.
    /// Returns Some translatedPacket ready to send via external gateway, or None if not applicable.
    let tryHandleOutbound (vpnSubnetUint: uint32) (vpnMaskUint: uint32) (serverPublicIpUint: uint32) (packet: byte[]) : byte[] option =
        match tryParseIcmpEchoRequest vpnSubnetUint vpnMaskUint packet with
        | None -> None
        | Some (srcIp, identifier, sequence) ->
            // Record the mapping
            let key = { identifier = identifier; sequence = sequence }
            let entry = { clientIp = srcIp; timestamp = DateTime.UtcNow }
            pendingRequests[key] <- entry

            // Make a copy of the packet to modify
            let outPacket = Array.copy packet
            let ihl = getIpHeaderLength outPacket

            // Rewrite source IP to server public IP
            writeUInt32 outPacket 12 serverPublicIpUint

            // Recompute checksums
            updateIpChecksum outPacket
            updateIcmpChecksum outPacket

            let dstIp = readUInt32 outPacket 16
            Logger.logTrace (fun () ->
                $"ICMPPROXY OUT: {ipToString srcIp} -> {ipToString dstIp}, id={identifier}, seq={sequence}")

            Some outPacket

    /// Handle inbound ICMP Echo Reply from the external network.
    /// Returns Some (clientIpUint, translatedPacket) if it matches a pending request, None otherwise.
    let tryHandleInbound (packet: byte[]) : (uint32 * byte[]) option =
        match tryParseIcmpEchoReply packet with
        | None -> None
        | Some (identifier, sequence) ->
            let key = { identifier = identifier; sequence = sequence }
            match pendingRequests.TryRemove(key) with
            | false, _ ->
                Logger.logTrace (fun () ->
                    $"ICMPPROXY IN: no mapping for id={identifier}, seq={sequence}, dropping")
                None
            | true, entry ->
                // Make a copy of the packet to modify
                let inPacket = Array.copy packet
                let ihl = getIpHeaderLength inPacket

                let srcIp = readUInt32 inPacket 12

                // Rewrite destination IP to the original client IP
                writeUInt32 inPacket 16 entry.clientIp

                // Recompute checksums
                updateIpChecksum inPacket
                updateIcmpChecksum inPacket

                Logger.logTrace (fun () ->
                    $"ICMPPROXY IN: {ipToString srcIp} -> {ipToString entry.clientIp}, id={identifier}, seq={sequence}")

                Some (entry.clientIp, inPacket)
