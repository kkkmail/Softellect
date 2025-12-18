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
        let mutable onPacketCallback : (byte[] -> unit) option = None
        let mutable receiveArgs : SocketAsyncEventArgs option = None
        let receiveBuffer : byte[] = Array.zeroCreate<byte> 65535

        // Precomputed server public IP bytes for fast comparison
        let serverIpBytes = config.serverPublicIp.GetAddressBytes()

        // Counters for stats
        let mutable totalReceived = 0L
        let mutable passedToCallback = 0L
        let mutable droppedTooShort = 0L
        let mutable droppedNotDstServerIp = 0L
        let mutable droppedSrcIsServerIp = 0L
        let mutable receiveErrors = 0L

        // Stopwatch for throttled logging
        let statsStopwatch = System.Diagnostics.Stopwatch()

        // Raw IP socket for external communication (handles both TCP and UDP)
        // NOTE: Requires administrative privileges on Windows.
        let rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP)

        let isShutdownError (error: SocketError) =
            match error with
            | SocketError.OperationAborted
            | SocketError.Interrupted
            | SocketError.NotSocket
            | SocketError.ConnectionReset
            | SocketError.Shutdown -> true
            | _ -> false

        let logStatsIfDue () =
            if statsStopwatch.ElapsedMilliseconds >= 5000L then
                let total = Interlocked.Read(&totalReceived)
                let passed = Interlocked.Read(&passedToCallback)
                let dropShort = Interlocked.Read(&droppedTooShort)
                let dropNotDst = Interlocked.Read(&droppedNotDstServerIp)
                let dropSrc = Interlocked.Read(&droppedSrcIsServerIp)
                let errors = Interlocked.Read(&receiveErrors)
                Logger.logInfo $"ExternalGateway stats: total={total}, passed={passed}, dropShort={dropShort}, dropNotDst={dropNotDst}, dropSrc={dropSrc}, errors={errors}"
                statsStopwatch.Restart()

        // Helper to queue work on ThreadPool to break inline recursion
        let queue (f: unit -> unit) = ThreadPool.UnsafeQueueUserWorkItem((fun _ -> f()), null) |> ignore

        let rec startReceive () =
            if running then
                match receiveArgs with
                | Some args ->
                    try
                        let pending = rawSocket.ReceiveAsync(args)
                        if not pending then
                            // Completed synchronously - queue to break inline recursion
                            queue (fun () -> handleCompleted args)
                    with
                    | :? ObjectDisposedException ->
                        () // Socket disposed during shutdown, ignore
                    | ex ->
                        if running then
                            Logger.logError $"ExternalGateway startReceive error: {ex.Message}"
                | None -> ()

        and handleCompleted (e: SocketAsyncEventArgs) =
            if not running then
                () // Exit immediately if stopped
            else
                match e.SocketError with
                | SocketError.Success when e.BytesTransferred > 0 ->
                    Interlocked.Increment(&totalReceived) |> ignore

                    // Drop if too short for IPv4 header
                    if e.BytesTransferred < 20 then
                        Interlocked.Increment(&droppedTooShort) |> ignore
                        logStatsIfDue()
                        queue startReceive
                    // Drop if destination IP is NOT the server public IP
                    elif receiveBuffer[16] <> serverIpBytes[0] ||
                         receiveBuffer[17] <> serverIpBytes[1] ||
                         receiveBuffer[18] <> serverIpBytes[2] ||
                         receiveBuffer[19] <> serverIpBytes[3] then
                        Interlocked.Increment(&droppedNotDstServerIp) |> ignore
                        logStatsIfDue()
                        queue startReceive
                    // Drop if source IP IS the server public IP (outbound/self traffic)
                    elif receiveBuffer[12] = serverIpBytes[0] &&
                         receiveBuffer[13] = serverIpBytes[1] &&
                         receiveBuffer[14] = serverIpBytes[2] &&
                         receiveBuffer[15] = serverIpBytes[3] then
                        Interlocked.Increment(&droppedSrcIsServerIp) |> ignore
                        logStatsIfDue()
                        queue startReceive
                    else
                        // Only now allocate/copy
                        let packet = Array.zeroCreate<byte> e.BytesTransferred
                        Array.Copy(receiveBuffer, packet, e.BytesTransferred)
                        Interlocked.Increment(&passedToCallback) |> ignore

                        // Invoke callback if present
                        match onPacketCallback with
                        | Some callback -> callback packet
                        | None -> ()

                        logStatsIfDue()
                        queue startReceive

                | SocketError.Success ->
                    // Zero bytes - stop the pump (do NOT re-arm to prevent infinite loop)
                    running <- false

                | error when isShutdownError error ->
                    // Expected shutdown error
                    if running then
                        Logger.logError $"ExternalGateway receive error during run: {error}"
                    // Don't re-issue receive on shutdown errors

                | error ->
                    // Unexpected error
                    if running then
                        Interlocked.Increment(&receiveErrors) |> ignore
                        Logger.logError $"ExternalGateway receive error: {error}"
                    logStatsIfDue()
                    // Re-issue receive on non-shutdown errors if still running
                    queue startReceive

        let onCompletedHandler = EventHandler<SocketAsyncEventArgs>(fun _ e -> handleCompleted e)

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
                statsStopwatch.Restart()

                // Create and configure SocketAsyncEventArgs
                let args = new SocketAsyncEventArgs()
                args.SetBuffer(receiveBuffer, 0, receiveBuffer.Length)
                args.Completed.AddHandler(onCompletedHandler)
                receiveArgs <- Some args

                // Start the receive pump
                startReceive ()

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
                        // Logger.logTrace (fun () -> $"HEAVY LOG - Sent {sent} bytes to rawSocket, packet: {(summarizePacket packet)}.")
                        ()
                    with
                    | ex -> Logger.logError $"ExternalGateway.sendOutbound: Failed to send packet: {(summarizePacket packet)}, exception: '{ex.Message}'."
                | None -> Logger.logWarn "ExternalGateway.sendOutbound: Could not extract destination IP, dropping packet"

        /// Stop the external gateway.
        member _.stop() =
            Logger.logInfo "ExternalGateway stopping"
            running <- false
            onPacketCallback <- None

            try
                rawSocket.Close()
                rawSocket.Dispose()
            with _ -> ()

            match receiveArgs with
            | Some args ->
                args.Completed.RemoveHandler(onCompletedHandler)
                args.Dispose()
                receiveArgs <- None
            | None -> ()

            Logger.logInfo "ExternalGateway stopped"

        interface IDisposable with
            member this.Dispose() = this.stop()
