namespace Softellect.Vpn.Server

open System
open System.Net
open System.Net.Sockets
open System.Threading

open Softellect.Sys.Logging
open Softellect.Vpn.Core.PacketDebug

/// User-space external interface for sending/receiving raw IPv4 packets to/from the real internet.
/// This module handles both TCP and UDP traffic by working at the raw IP packet level.
/// The NAT module handles all header rewriting; this module only sends/receives complete IPv4 packets.
///
/// NOTE: Raw sockets on Windows require administrative privileges.
/// This is acceptable for a VPN server running as a Windows service.
module ExternalInterface =

    // ---- IPv4 parsing helpers ----

    /// Extract destination IP address from IPv4 header (bytes 16-19) as IPAddress
    let private getDestinationIpAddress (packet: byte[]) =
        if packet.Length >= 20 then
            // IPv4 destination IP is at bytes 16-19 in network byte order
            let bytes = [| packet[16]; packet[17]; packet[18]; packet[19] |]
            Some (IPAddress(bytes))
        else
            None

    /// Extract source IP address from IPv4 header (bytes 12-15) as IPAddress
    let private getSourceIpAddress (packet: byte[]) =
        if packet.Length >= 16 then
            let bytes = [| packet[12]; packet[13]; packet[14]; packet[15] |]
            Some (IPAddress(bytes))
        else
            None

    /// Get protocol byte from IPv4 header (byte 9)
    let private getProtocol (packet: byte[]) =
        if packet.Length > 9 then Some packet[9] else None

    /// Get total length from IPv4 header (bytes 2-3)
    let private getTotalLength (packet: byte[]) =
        if packet.Length >= 4 then
            int ((uint16 packet[2] <<< 8) ||| uint16 packet[3])
        else
            0

    // ---- Configuration ----

    type ExternalConfig =
        {
            serverPublicIp : IPAddress
        }

    // ---- External Gateway ----

    /// Raw IPv4 gateway for sending/receiving complete IP packets to/from the external network.
    /// Uses SocketType.Raw with ProtocolType.IP to handle both TCP and UDP uniformly.
    type ExternalGateway(config: ExternalConfig) =
        let mutable running = false
        let mutable receiveThread : Thread option = None
        let mutable onPacketCallback : (byte[] -> unit) option = None

        // Raw IP socket for external communication (handles both TCP and UDP)
        // NOTE: Requires administrative privileges on Windows.
        let rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP)

        let receiveLoop () =
            let buffer = Array.zeroCreate<byte> 65535

            while running do
                try
                    // Use Poll to allow checking the running flag periodically
                    if rawSocket.Poll(100000, SelectMode.SelectRead) then
                        let received = rawSocket.Receive(buffer)
                        if received > 0 then
                            // Trim to actual packet size
                            let packet = Array.sub buffer 0 received

                            // Get protocol byte from IPv4 header
                            let protocol = if packet.Length > 9 then packet[9] else 0uy

                            match protocol with
                            | 17uy -> // UDP
                                // Full logging for debugging
                                Logger.logTrace (fun () -> $"HEAVY LOG - ExternalGateway (UDP): Received raw IP packet, len={packet.Length}, packet=%A{(summarizePacket packet)}")

                                // Forward full IPv4 packet to NAT
                                match onPacketCallback with
                                | Some callback -> callback packet
                                | None -> ()

                            | 6uy -> // TCP
                                // Full logging for debugging
                                // Logger.logTrace (fun () -> $"HEAVY LOG - ExternalGateway (TCP): Received raw IP packet, len={packet.Length}, packet=%A{(summarizePacket packet)}")

                                // Forward full IPv4 packet to NAT
                                match onPacketCallback with
                                | Some callback -> callback packet
                                | None -> ()

                            | 1uy -> // ICMP
                                // Forward ICMP to callback for ICMP proxy handling
                                Logger.logTrace (fun () -> $"HEAVY LOG - ExternalGateway (ICMP): Received raw IP packet, len={packet.Length}, packet=%A{(summarizePacket packet)}")

                                match onPacketCallback with
                                | Some callback -> callback packet
                                | None -> ()

                            | _ ->
                                // Silently drop other protocols
                                ()
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
            // Configure raw socket for sending complete IP packets (including IP header)
            rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true)

            // Bind to the server's external IP
            let endPoint = IPEndPoint(config.serverPublicIp, 0)
            rawSocket.Bind(endPoint)

            // CRITICAL: Enable ReceiveAll mode on Windows to receive ALL IPv4 packets (TCP + UDP)
            // Without this, raw sockets on Windows only receive TCP packets, not UDP.
            // This is equivalent to SIO_RCVALL (0x98000001)
            let inVal = BitConverter.GetBytes(1)        // enable = 1
            let outVal = Array.zeroCreate<byte> 4
            try
                rawSocket.IOControl(IOControlCode.ReceiveAll, inVal, outVal) |> ignore
                Logger.logInfo "ExternalGateway: IOControl ReceiveAll enabled"
            with
            | ex ->
                Logger.logWarn $"ExternalGateway: IOControl ReceiveAll failed: {ex.Message}. UDP may not work."

            rawSocket.ReceiveTimeout <- 1000
            Logger.logInfo $"ExternalGateway: Raw IP socket bound to {config.serverPublicIp}"

        /// Start the background receive loop.
        /// onPacketFromInternet is called when a raw IP packet arrives from the external network.
        /// The caller (PacketRouter) should pass these through NAT.translateInbound before injection.
        member _.start(onPacketFromInternet: byte[] -> unit) =
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

                Logger.logInfo "ExternalGateway started (raw IP mode, TCP+UDP)"

        /// Send a NATted outbound packet to the external network.
        /// The packet must be a complete IPv4 packet already processed by NAT
        /// (source IP/port rewritten to external address).
        ///
        /// This method sends the entire IPv4 packet as-is via the raw socket.
        /// Both TCP and UDP packets are handled uniformly at the raw IP level.
        member _.sendOutbound(packet: byte[]) =
            if packet.Length < 20 then
                Logger.logWarn "ExternalGateway.sendOutbound: Packet too short (< 20 bytes), dropping"
            else
                match getDestinationIpAddress packet with
                | Some dstIp ->
                    // For raw sockets with HeaderIncluded, we still need to specify a destination
                    // endpoint for routing purposes. Port 0 is used since we're at the IP level.
                    let remoteEndPoint = IPEndPoint(dstIp, 0)

                    try
                        let sent = rawSocket.SendTo(packet, remoteEndPoint)
                        Logger.logTrace (fun () -> $"Sent {sent} bytes to rawSocket, packet: {(summarizePacket packet)}.")
                        ()
                    with
                    | ex -> Logger.logError $"ExternalGateway.sendOutbound: Failed to send packet: {ex.Message}"
                | None -> Logger.logWarn "ExternalGateway.sendOutbound: Could not extract destination IP, dropping packet"

        /// Stop the external gateway.
        member _.stop() =
            Logger.logInfo "ExternalGateway stopping"
            running <- false

            match receiveThread with
            | Some thread ->
                if thread.IsAlive then
                    thread.Join(TimeSpan.FromSeconds(5.0)) |> ignore
                receiveThread <- None
            | None -> ()

            try
                rawSocket.Close()
                rawSocket.Dispose()
            with _ -> ()

            Logger.logInfo "ExternalGateway stopped"

        interface IDisposable with
            member this.Dispose() = this.stop()
