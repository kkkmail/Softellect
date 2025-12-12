namespace Softellect.Vpn.Server

open System
open System.Net
open System.Net.Sockets

open Softellect.Sys.Logging

/// Minimal DNS proxy for VPN gateway.
/// Intercepts UDP packets to serverVpnIp:53, forwards to upstream DNS, and builds reply packets.
module DnsProxy =

    // ---- Configuration ----

    /// Upstream DNS server (hardcoded for MVP, can be made configurable later)
    let private upstreamDns = IPEndPoint(IPAddress.Parse("1.1.1.1"), 53)

    /// Timeout for upstream DNS response
    let private dnsTimeoutMs = 2000

    // ---- IPv4/UDP helpers ----

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

    /// Compute UDP checksum (pseudo-header + UDP header + payload)
    let private computeUdpChecksum (srcIp: uint32) (dstIp: uint32) (udpHeader: byte[]) (payload: byte[]) =
        let udpLen = 8 + payload.Length
        let mutable sum = 0u

        // Pseudo-header
        sum <- sum + uint32 (uint16 (srcIp >>> 16))
        sum <- sum + uint32 (uint16 (srcIp &&& 0xFFFFu))
        sum <- sum + uint32 (uint16 (dstIp >>> 16))
        sum <- sum + uint32 (uint16 (dstIp &&& 0xFFFFu))
        sum <- sum + 17u // UDP protocol
        sum <- sum + uint32 udpLen

        // UDP header (skip checksum field at offset 6-7)
        sum <- sum + uint32 (readUInt16 udpHeader 0) // src port
        sum <- sum + uint32 (readUInt16 udpHeader 2) // dst port
        sum <- sum + uint32 (readUInt16 udpHeader 4) // length
        // checksum field is 0 during computation

        // Payload
        let mutable i = 0
        while i < payload.Length - 1 do
            sum <- sum + uint32 ((uint16 payload[i] <<< 8) ||| uint16 payload[i + 1])
            i <- i + 2
        if i < payload.Length then
            sum <- sum + uint32 (uint16 payload[i] <<< 8) // odd byte

        // fold carries
        while (sum >>> 16) <> 0u do
            sum <- (sum &&& 0xFFFFu) + (sum >>> 16)

        let result = uint16 (~~~sum &&& 0xFFFFu)
        // UDP checksum 0 means "no checksum", use 0xFFFF instead
        if result = 0us then 0xFFFFus else result

    /// Build an IPv4/UDP reply packet
    let private buildUdpReply (srcIp: uint32) (srcPort: uint16) (dstIp: uint32) (dstPort: uint16) (payload: byte[]) =
        let ipHeaderLen = 20
        let udpHeaderLen = 8
        let totalLen = ipHeaderLen + udpHeaderLen + payload.Length

        let packet = Array.zeroCreate<byte> totalLen

        // IPv4 header
        packet[0] <- 0x45uy // Version 4, IHL 5
        packet[1] <- 0uy    // DSCP/ECN
        writeUInt16 packet 2 (uint16 totalLen) // Total length
        writeUInt16 packet 4 0us // Identification
        packet[6] <- 0x40uy // Flags: Don't Fragment
        packet[7] <- 0uy    // Fragment offset
        packet[8] <- 64uy   // TTL
        packet[9] <- 17uy   // Protocol: UDP

        // Checksum placeholder
        writeUInt16 packet 10 0us

        // Source IP
        writeUInt32 packet 12 srcIp

        // Destination IP
        writeUInt32 packet 16 dstIp

        // Compute and set IP checksum
        let ipCsum = computeIpChecksum packet ipHeaderLen
        writeUInt16 packet 10 ipCsum

        // UDP header
        writeUInt16 packet 20 srcPort // Source port
        writeUInt16 packet 22 dstPort // Destination port
        writeUInt16 packet 24 (uint16 (udpHeaderLen + payload.Length)) // UDP length
        writeUInt16 packet 26 0us // Checksum placeholder

        // Copy payload
        Array.Copy(payload, 0, packet, 28, payload.Length)

        // Compute and set UDP checksum
        let udpHeader = packet[20..27]
        let udpCsum = computeUdpChecksum srcIp dstIp udpHeader payload
        writeUInt16 packet 26 udpCsum

        packet

    // ---- DNS Proxy Logic ----

    /// Check if this packet is a DNS query to the VPN gateway (serverVpnIp:53)
    /// Returns Some (srcIp, srcPort, dnsPayload) if it's a DNS query, None otherwise
    let tryParseDnsQuery (serverVpnIpUint: uint32) (packet: byte[]) : (uint32 * uint16 * byte[]) option =
        if packet.Length < 28 then None // Min: 20 IP + 8 UDP
        else
            let version = int packet[0] >>> 4
            if version <> 4 then None
            else
                let ihl = getIpHeaderLength packet
                if packet.Length < ihl + 8 then None
                else
                    let protocol = getProtocol packet
                    if protocol <> 17uy then None // Not UDP
                    else
                        let dstIp = readUInt32 packet 16
                        let dstPort = readUInt16 packet (ihl + 2)

                        if dstIp <> serverVpnIpUint || dstPort <> 53us then None
                        else
                            let srcIp = readUInt32 packet 12
                            let srcPort = readUInt16 packet ihl
                            let udpLen = int (readUInt16 packet (ihl + 4))
                            let payloadLen = udpLen - 8

                            if payloadLen <= 0 || packet.Length < ihl + 8 + payloadLen then None
                            else
                                let payload = Array.sub packet (ihl + 8) payloadLen
                                Some (srcIp, srcPort, payload)

    /// Forward DNS query to upstream and return reply packet ready for injection.
    /// Returns Some replyPacket or None on timeout/error.
    let forwardDnsQuery (serverVpnIpUint: uint32) (clientIp: uint32) (clientPort: uint16) (dnsQuery: byte[]) : byte[] option =
        let clientIpStr =
            $"{byte (clientIp >>> 24)}.{byte (clientIp >>> 16)}.{byte (clientIp >>> 8)}.{byte clientIp}"

        Logger.logTrace (fun () ->
            $"DNSPROXY OUT: {clientIpStr}:{clientPort} -> 10.66.77.1:53 qlen={dnsQuery.Length} upstream={upstreamDns}")

        try
            use udpClient = new UdpClient()
            udpClient.Client.ReceiveTimeout <- dnsTimeoutMs

            // Send query to upstream DNS
            udpClient.Send(dnsQuery, dnsQuery.Length, upstreamDns) |> ignore

            // Receive response
            let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
            let response = udpClient.Receive(&remoteEp)

            Logger.logTrace (fun () ->
                $"DNSPROXY IN: upstream response len={response.Length} -> {clientIpStr}:{clientPort}")

            // Build reply packet: src=serverVpnIp:53, dst=clientIp:clientPort
            let replyPacket = buildUdpReply serverVpnIpUint 53us clientIp clientPort response
            Some replyPacket

        with
        | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
            Logger.logTrace (fun () ->
                $"DNSPROXY TIMEOUT: no response from upstream for {clientIpStr}:{clientPort}")
            None
        | ex ->
            Logger.logWarn $"DNSPROXY ERROR: {ex.Message}"
            None
