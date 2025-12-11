namespace Softellect.Vpn.Server

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Collections.Concurrent

open Softellect.Sys.Logging

/// User-space external interface for sending/receiving packets to/from the real internet.
/// This module handles UDP traffic forwarding. TCP support is stubbed for v1.
module ExternalInterface =

    // ---- IPv4/UDP parsing helpers ----

    let private readUInt16 (buf: byte[]) (offset: int) =
        (uint16 buf[offset] <<< 8) ||| uint16 buf[offset + 1]

    let private readUInt32 (buf: byte[]) (offset: int) =
        (uint32 buf[offset] <<< 24) |||
        (uint32 buf[offset + 1] <<< 16) |||
        (uint32 buf[offset + 2] <<< 8) |||
        uint32 buf[offset + 3]

    let private headerLength (buf: byte[]) =
        let ihl = int (buf[0] &&& 0x0Fuy)
        ihl * 4

    let private getProtocol (buf: byte[]) =
        buf[9]

    let private getDestinationIp (buf: byte[]) =
        readUInt32 buf 16

    let private getDestinationPort (buf: byte[]) =
        let ihl = headerLength buf
        readUInt16 buf (ihl + 2) // destination port is at offset 2 in UDP header

    let private getSourcePort (buf: byte[]) =
        let ihl = headerLength buf
        readUInt16 buf ihl // source port is at offset 0 in UDP header

    let private getUdpPayloadOffset (buf: byte[]) =
        let ihl = headerLength buf
        ihl + 8 // IP header + UDP header (8 bytes)

    let private getUdpPayloadLength (buf: byte[]) =
        let ihl = headerLength buf
        let udpLen = readUInt16 buf (ihl + 4) // UDP length field
        int udpLen - 8 // subtract UDP header

    let private uint32ToIPAddress (ip: uint32) =
        let bytes = [|
            byte (ip >>> 24)
            byte (ip >>> 16)
            byte (ip >>> 8)
            byte ip
        |]
        IPAddress(bytes)

    // ---- Configuration ----

    type ExternalConfig =
        {
            serverPublicIp : IPAddress
        }

    // ---- External Gateway ----

    /// Key for tracking UDP "connections" for replies
    [<Struct>]
    type private UdpFlowKey =
        {
            remoteIp : uint32
            remotePort : uint16
            localPort : uint16
        }

    type ExternalGateway(config: ExternalConfig) =
        let mutable running = false
        let mutable receiveThread : Thread option = None
        let mutable onPacketCallback : (byte[] -> unit) option = None

        // UDP socket for external communication
        let udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)

        // Track flows so we can reconstruct IP packets for replies
        // Key: (remoteIp, remotePort, localPort) -> original source info for NAT reverse
        let activeFlows = ConcurrentDictionary<UdpFlowKey, DateTime>()

        let cleanupStaleFlows () =
            let now = DateTime.UtcNow
            let staleTime = TimeSpan.FromMinutes(5.0)
            for kv in activeFlows do
                if now - kv.Value > staleTime then
                    activeFlows.TryRemove(kv.Key) |> ignore

        /// Build an IPv4/UDP packet from received UDP data
        let buildIpUdpPacket (srcIp: IPAddress) (srcPort: uint16) (dstIp: IPAddress) (dstPort: uint16) (payload: byte[]) =
            let ipHeaderLen = 20
            let udpHeaderLen = 8
            let totalLen = ipHeaderLen + udpHeaderLen + payload.Length

            let packet = Array.zeroCreate<byte> totalLen

            // IPv4 header
            packet[0] <- 0x45uy // Version (4) + IHL (5)
            packet[1] <- 0uy    // DSCP/ECN
            packet[2] <- byte (totalLen >>> 8)
            packet[3] <- byte totalLen
            packet[4] <- 0uy    // Identification
            packet[5] <- 0uy
            packet[6] <- 0x40uy // Flags (Don't Fragment)
            packet[7] <- 0uy
            packet[8] <- 64uy   // TTL
            packet[9] <- 17uy   // Protocol: UDP

            // Checksum placeholder (will compute after)
            packet[10] <- 0uy
            packet[11] <- 0uy

            // Source IP
            let srcBytes = srcIp.GetAddressBytes()
            packet[12] <- srcBytes[0]
            packet[13] <- srcBytes[1]
            packet[14] <- srcBytes[2]
            packet[15] <- srcBytes[3]

            // Destination IP
            let dstBytes = dstIp.GetAddressBytes()
            packet[16] <- dstBytes[0]
            packet[17] <- dstBytes[1]
            packet[18] <- dstBytes[2]
            packet[19] <- dstBytes[3]

            // Compute IP header checksum
            let mutable sum = 0u
            for i in 0 .. 2 .. 18 do
                if i <> 10 then
                    sum <- sum + uint32 ((uint16 packet[i] <<< 8) ||| uint16 packet[i + 1])
            while (sum >>> 16) <> 0u do
                sum <- (sum &&& 0xFFFFu) + (sum >>> 16)
            let checksum = uint16 (~~~sum &&& 0xFFFFu)
            packet[10] <- byte (checksum >>> 8)
            packet[11] <- byte checksum

            // UDP header
            packet[20] <- byte (srcPort >>> 8)
            packet[21] <- byte srcPort
            packet[22] <- byte (dstPort >>> 8)
            packet[23] <- byte dstPort

            let udpLen = uint16 (udpHeaderLen + payload.Length)
            packet[24] <- byte (udpLen >>> 8)
            packet[25] <- byte udpLen

            // UDP checksum (optional for IPv4, set to 0)
            packet[26] <- 0uy
            packet[27] <- 0uy

            // Payload
            Array.Copy(payload, 0, packet, 28, payload.Length)

            packet

        let receiveLoop () =
            let buffer = Array.zeroCreate<byte> 65536
            let mutable remoteEp : EndPoint = IPEndPoint(IPAddress.Any, 0)

            while running do
                try
                    if udpSocket.Available > 0 || udpSocket.Poll(100000, SelectMode.SelectRead) then
                        let received = udpSocket.ReceiveFrom(buffer, &remoteEp)
                        if received > 0 then
                            let remoteIpEp = remoteEp :?> IPEndPoint
                            let payload = Array.sub buffer 0 received

                            // We need to figure out which local port this reply is for.
                            // The reply's destination is the port we bound/sent from.
                            // Since we're using a single socket, all outbound goes from the same ephemeral port.
                            let localPort = (udpSocket.LocalEndPoint :?> IPEndPoint).Port |> uint16

                            // Build an IP packet with:
                            // - src = remote internet host
                            // - dst = our external IP (NAT will translate back to VPN client)
                            let remoteIpBytes = remoteIpEp.Address.GetAddressBytes()
                            let srcIp = remoteIpEp.Address
                            let srcPort = uint16 remoteIpEp.Port
                            let dstIp = config.serverPublicIp
                            let dstPort = localPort

                            let ipPacket = buildIpUdpPacket srcIp srcPort dstIp dstPort payload

                            Logger.logTrace (fun () ->
                                $"ExternalGateway: Received UDP from {remoteIpEp}, building IP packet of {ipPacket.Length} bytes")

                            match onPacketCallback with
                            | Some callback -> callback ipPacket
                            | None -> ()
                with
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                    () // Timeout is expected, continue
                | :? ObjectDisposedException ->
                    running <- false
                | ex ->
                    if running then
                        Logger.logError $"ExternalGateway receive error: {ex.Message}"
                        Thread.Sleep(100)

        do
            // Bind UDP socket to any available port on all interfaces
            udpSocket.Bind(IPEndPoint(IPAddress.Any, 0))
            udpSocket.ReceiveTimeout <- 1000
            Logger.logInfo $"ExternalGateway: UDP socket bound to {udpSocket.LocalEndPoint}"

        /// Start the background receive loop.
        /// onPacketFromInternet is called when a packet arrives from the external network.
        member _.Start(onPacketFromInternet: byte[] -> unit) =
            if running then
                Logger.logWarn "ExternalGateway already running"
            else
                onPacketCallback <- Some onPacketFromInternet
                running <- true

                let thread = Thread(ThreadStart(receiveLoop))
                thread.IsBackground <- true
                thread.Name <- "ExternalGateway-Receive"
                thread.Start()
                receiveThread <- Some thread

                Logger.logInfo "ExternalGateway started"

        /// Send a NATted outbound packet to the external network.
        /// The packet should already have been processed by NAT (source IP/port rewritten to external).
        member _.SendOutbound(packet: byte[]) =
            if packet.Length < 20 then
                Logger.logWarn "ExternalGateway: Packet too short, dropping"
            else
                let protocol = getProtocol packet
                match protocol with
                | 17uy -> // UDP
                    let ihl = headerLength packet
                    if packet.Length < ihl + 8 then
                        Logger.logWarn "ExternalGateway: UDP packet too short, dropping"
                    else
                        let dstIp = getDestinationIp packet
                        let dstPort = getDestinationPort packet
                        let srcPort = getSourcePort packet

                        let payloadOffset = getUdpPayloadOffset packet
                        let payloadLen = getUdpPayloadLength packet

                        if payloadOffset + payloadLen <= packet.Length && payloadLen >= 0 then
                            let payload = Array.sub packet payloadOffset payloadLen
                            let dstIpAddr = uint32ToIPAddress dstIp
                            let dstEndpoint = IPEndPoint(dstIpAddr, int dstPort)

                            try
                                let sent = udpSocket.SendTo(payload, dstEndpoint)

                                // Track flow for replies
                                let flowKey = { remoteIp = dstIp; remotePort = dstPort; localPort = srcPort }
                                activeFlows[flowKey] <- DateTime.UtcNow

                                Logger.logTrace (fun () ->
                                    $"ExternalGateway: Sent {sent} bytes UDP to {dstEndpoint}")
                            with
                            | ex ->
                                Logger.logError $"ExternalGateway: Failed to send UDP: {ex.Message}"
                        else
                            Logger.logWarn $"ExternalGateway: Invalid UDP payload length, dropping"

                | 6uy -> // TCP
                    // TCP forwarding is not implemented in v1
                    Logger.logTrace (fun () -> "ExternalGateway: TCP forwarding not implemented, dropping packet")

                | _ ->
                    Logger.logTrace (fun () -> $"ExternalGateway: Unsupported protocol {protocol}, dropping packet")

        /// Stop the external gateway.
        member _.Stop() =
            Logger.logInfo "ExternalGateway stopping"
            running <- false

            match receiveThread with
            | Some thread ->
                if thread.IsAlive then
                    thread.Join(TimeSpan.FromSeconds(5.0)) |> ignore
                receiveThread <- None
            | None -> ()

            try
                udpSocket.Close()
                udpSocket.Dispose()
            with _ -> ()

            activeFlows.Clear()
            Logger.logInfo "ExternalGateway stopped"

        interface IDisposable with
            member this.Dispose() = this.Stop()
