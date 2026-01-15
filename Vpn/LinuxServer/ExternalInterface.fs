namespace Softellect.Vpn.Server

open System
open System.Net
open System.Net.Sockets
open System.Threading

open Softellect.Sys.Logging
open Softellect.Vpn.Core.PacketDebug
open Softellect.Vpn.Core.UdpProtocol

/// Linux external interface for sending/receiving raw IPv4 packets to/from the real internet.
/// Linux does NOT support SocketType.Raw + ProtocolType.IP (EPROTONOSUPPORT).
/// Instead, use two raw sockets: one for TCP and one for UDP.
module ExternalInterface =

    // ---- IPv4 parsing helpers ----

    let private getDestinationIpAddress (packet: byte[]) =
        if packet.Length >= 20 then
            Some (IPAddress([| packet[16]; packet[17]; packet[18]; packet[19] |]))
        else
            None

    let private getProtocolByte (packet: byte[]) =
        if packet.Length > 9 then Some packet[9] else None

    // ---- Configuration ----

    type ExternalConfig =
        {
            serverPublicIp : IPAddress
        }

    // ---- External Gateway ----

    type ExternalGateway(config: ExternalConfig) =

        let mutable running = false
        let mutable onPacketCallback : (byte[] -> unit) option = None

        // Precomputed server public IP bytes for fast comparison
        let serverIpBytes = config.serverPublicIp.GetAddressBytes()

        // Stats
        let mutable totalReceived = 0L
        let mutable passedToCallback = 0L
        let mutable droppedTooShort = 0L
        let mutable droppedNotDstServerIp = 0L
        let mutable droppedSrcIsServerIp = 0L
        let mutable receiveErrors = 0L

        let statsStopwatch = System.Diagnostics.Stopwatch()

        let logStatsIfDue () =
            if statsStopwatch.ElapsedMilliseconds >= PushStatsIntervalMs then
                let total  = Interlocked.Read(&totalReceived)
                let passed = Interlocked.Read(&passedToCallback)
                let d1     = Interlocked.Read(&droppedTooShort)
                let d2     = Interlocked.Read(&droppedNotDstServerIp)
                let d3     = Interlocked.Read(&droppedSrcIsServerIp)
                let errs   = Interlocked.Read(&receiveErrors)
                Logger.logInfo $"ExternalGateway(Linux) stats: total={total}, passed={passed}, dropShort={d1}, dropNotDst={d2}, dropSrc={d3}, errors={errs}"
                statsStopwatch.Restart()

        let isShutdownError (error: SocketError) =
            match error with
            | SocketError.OperationAborted
            | SocketError.Interrupted
            | SocketError.NotSocket
            | SocketError.ConnectionReset
            | SocketError.Shutdown -> true
            | _ -> false

        // Helper to queue work on ThreadPool to break inline recursion
        let queue (f: unit -> unit) =
            ThreadPool.UnsafeQueueUserWorkItem((fun _ -> f()), null) |> ignore

        // Two raw sockets: TCP + UDP
        let rawTcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Tcp)
        let rawUdpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Udp)

        // Separate buffers/args per socket
        let tcpBuffer : byte[] = Array.zeroCreate<byte> 65535
        let udpBuffer : byte[] = Array.zeroCreate<byte> 65535

        let mutable tcpArgs : SocketAsyncEventArgs option = None
        let mutable udpArgs : SocketAsyncEventArgs option = None

        let shouldDropByIpFilter (buffer: byte[]) (len: int) =
            // Drop if too short for IPv4 header
            if len < 20 then
                Interlocked.Increment(&droppedTooShort) |> ignore
                true
            // Drop if destination IP is NOT the server public IP
            elif buffer[16] <> serverIpBytes[0] ||
                 buffer[17] <> serverIpBytes[1] ||
                 buffer[18] <> serverIpBytes[2] ||
                 buffer[19] <> serverIpBytes[3] then
                Interlocked.Increment(&droppedNotDstServerIp) |> ignore
                true
            // Drop if source IP IS the server public IP (outbound/self traffic)
            elif buffer[12] = serverIpBytes[0] &&
                 buffer[13] = serverIpBytes[1] &&
                 buffer[14] = serverIpBytes[2] &&
                 buffer[15] = serverIpBytes[3] then
                Interlocked.Increment(&droppedSrcIsServerIp) |> ignore
                true
            else
                false

        let handleCompleted (which: string) (buffer: byte[]) (startReceive: unit -> unit) (e: SocketAsyncEventArgs) =
            if not running then
                ()
            else
                match e.SocketError with
                | SocketError.Success when e.BytesTransferred > 0 ->
                    Interlocked.Increment(&totalReceived) |> ignore

                    if shouldDropByIpFilter buffer e.BytesTransferred then
                        logStatsIfDue()
                        queue startReceive
                    else
                        let packet = Array.zeroCreate<byte> e.BytesTransferred
                        Array.Copy(buffer, packet, e.BytesTransferred)
                        Interlocked.Increment(&passedToCallback) |> ignore

                        match onPacketCallback with
                        | Some cb -> cb packet
                        | None -> ()

                        logStatsIfDue()
                        queue startReceive

                | SocketError.Success ->
                    // Zero bytes - stop the pump
                    running <- false

                | err when isShutdownError err ->
                    if running then
                        Logger.logError $"ExternalGateway(Linux) {which} receive error during run: {err}"
                    // no re-arm

                | err ->
                    if running then
                        Interlocked.Increment(&receiveErrors) |> ignore
                        Logger.logError $"ExternalGateway(Linux) {which} receive error: {err}"
                    logStatsIfDue()
                    queue startReceive

        let rec startReceiveTcp () =
            if running then
                match tcpArgs with
                | Some args ->
                    try
                        let pending = rawTcpSocket.ReceiveAsync(args)
                        if not pending then queue (fun () -> handleCompleted "TCP" tcpBuffer startReceiveTcp args)
                    with
                    | :? ObjectDisposedException -> ()
                    | ex -> if running then Logger.logError $"ExternalGateway(Linux) TCP startReceive error: {ex.Message}"
                | None -> ()

        let rec startReceiveUdp () =
            if running then
                match udpArgs with
                | Some args ->
                    try
                        let pending = rawUdpSocket.ReceiveAsync(args)
                        if not pending then queue (fun () -> handleCompleted "UDP" udpBuffer startReceiveUdp args)
                    with
                    | :? ObjectDisposedException -> ()
                    | ex -> if running then Logger.logError $"ExternalGateway(Linux) UDP startReceive error: {ex.Message}"
                | None -> ()

        let tcpCompletedHandler =
            EventHandler<SocketAsyncEventArgs>(fun _ e -> handleCompleted "TCP" tcpBuffer startReceiveTcp e)

        let udpCompletedHandler =
            EventHandler<SocketAsyncEventArgs>(fun _ e -> handleCompleted "UDP" udpBuffer startReceiveUdp e)

        do
            // For sending complete IPv4 packets (including IP header)
            rawTcpSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true)
            rawUdpSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true)

            // Bind to the server external IP (port 0 because raw)
            let ep = IPEndPoint(config.serverPublicIp, 0)
            rawTcpSocket.Bind(ep)
            rawUdpSocket.Bind(ep)

            Logger.logInfo $"ExternalGateway(Linux): raw TCP+UDP sockets bound to {config.serverPublicIp}"

        member _.start(onPacketFromInternet: byte[] -> unit) =
            if running then
                Logger.logWarn "ExternalGateway(Linux) already running"
            else
                onPacketCallback <- Some onPacketFromInternet
                running <- true
                statsStopwatch.Restart()

                let ta = new SocketAsyncEventArgs()
                ta.SetBuffer(tcpBuffer, 0, tcpBuffer.Length)
                ta.Completed.AddHandler(tcpCompletedHandler)
                tcpArgs <- Some ta

                let ua = new SocketAsyncEventArgs()
                ua.SetBuffer(udpBuffer, 0, udpBuffer.Length)
                ua.Completed.AddHandler(udpCompletedHandler)
                udpArgs <- Some ua

                startReceiveTcp()
                startReceiveUdp()

                Logger.logInfo "ExternalGateway(Linux) started (raw TCP + raw UDP)"

        member _.sendOutbound(packet: byte[]) =
            if packet.Length < 20 then
                Logger.logWarn "ExternalGateway(Linux).sendOutbound: Packet too short (< 20 bytes), dropping"
            else
                match getDestinationIpAddress packet, getProtocolByte packet with
                | Some dstIp, Some proto ->
                    let remoteEndPoint = IPEndPoint(dstIp, 0)
                    try
                        // IPv4 protocol: TCP=6, UDP=17. Others: drop (or extend later).
                        match proto with
                        | 6uy  -> rawTcpSocket.SendTo(packet, remoteEndPoint) |> ignore
                        | 17uy -> rawUdpSocket.SendTo(packet, remoteEndPoint) |> ignore
                        | _ ->
                            Logger.logTrace (fun () -> $"ExternalGateway(Linux).sendOutbound: unsupported ip proto={proto}, drop. Packet: {summarizePacket packet}")
                    with ex ->
                        if getDstIp4 packet <> "255.255.255.255" then
                            Logger.logError $"ExternalGateway(Linux).sendOutbound failed: {(summarizePacket packet)}, exception: '{ex.Message}'."
                | _ ->
                    Logger.logWarn "ExternalGateway(Linux).sendOutbound: Could not extract dst/proto, dropping packet"

        member _.stop() =
            Logger.logInfo "ExternalGateway(Linux) stopping"
            running <- false
            onPacketCallback <- None

            try rawTcpSocket.Close(); rawTcpSocket.Dispose() with _ -> ()
            try rawUdpSocket.Close(); rawUdpSocket.Dispose() with _ -> ()

            match tcpArgs with
            | Some a ->
                a.Completed.RemoveHandler(tcpCompletedHandler)
                a.Dispose()
                tcpArgs <- None
            | None -> ()

            match udpArgs with
            | Some a ->
                a.Completed.RemoveHandler(udpCompletedHandler)
                a.Dispose()
                udpArgs <- None
            | None -> ()

            Logger.logInfo "ExternalGateway(Linux) stopped"

        interface IDisposable with
            member this.Dispose() = this.stop()
