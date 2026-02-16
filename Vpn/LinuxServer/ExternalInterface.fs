namespace Softellect.Vpn.Server

open System
open System.Net
open System.Net.Sockets
open System.Threading

open Softellect.Sys.Logging
open Softellect.Vpn.Core.PacketDebug
open Softellect.Transport.UdpProtocol

/// Linux external interface for sending/receiving raw IPv4 packets to/from the real internet.
/// Linux does NOT support SocketType.Raw + ProtocolType.IP (EPROTONOSUPPORT).
/// Instead, use three raw sockets: one for TCP, one for UDP, and one for ICMP.
///
/// IMPORTANT: On Linux, raw sockets with IPPROTO_TCP/IPPROTO_UDP/IPPROTO_ICMP receive packets
/// WITH the IP header included (unlike some documentation suggests).
/// However, packets are only delivered to raw sockets if they are destined to the local machine.
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
        let mutable totalTcpReceived = 0L
        let mutable totalUdpReceived = 0L
        let mutable totalIcmpReceived = 0L
        let mutable passedToCallback = 0L
        let mutable droppedTooShort = 0L
        let mutable droppedNotDstServerIp = 0L
        let mutable droppedSrcIsServerIp = 0L
        let mutable tcpReceiveErrors = 0L
        let mutable udpReceiveErrors = 0L
        let mutable icmpReceiveErrors = 0L

        let statsStopwatch = System.Diagnostics.Stopwatch()

        let logStatsIfDue () =
            if statsStopwatch.ElapsedMilliseconds >= PushStatsIntervalMs then
                let tcpRx  = Interlocked.Read(&totalTcpReceived)
                let udpRx  = Interlocked.Read(&totalUdpReceived)
                let icmpRx = Interlocked.Read(&totalIcmpReceived)
                let passed = Interlocked.Read(&passedToCallback)
                let d1     = Interlocked.Read(&droppedTooShort)
                let d2     = Interlocked.Read(&droppedNotDstServerIp)
                let d3     = Interlocked.Read(&droppedSrcIsServerIp)
                let tcpErr = Interlocked.Read(&tcpReceiveErrors)
                let udpErr = Interlocked.Read(&udpReceiveErrors)
                let icmpErr = Interlocked.Read(&icmpReceiveErrors)
                Logger.logInfo $"ExternalGateway(Linux) stats: tcpRx={tcpRx}, udpRx={udpRx}, icmpRx={icmpRx}, passed={passed}, dropShort={d1}, dropNotDst={d2}, dropSrc={d3}, tcpErr={tcpErr}, udpErr={udpErr}, icmpErr={icmpErr}"
                statsStopwatch.Restart()

        let isShutdownError (error: SocketError) =
            match error with
            | SocketError.OperationAborted
            | SocketError.Interrupted
            | SocketError.NotSocket
            | SocketError.ConnectionReset
            | SocketError.Shutdown -> true
            | _ -> false

        // Three raw sockets: TCP + UDP + ICMP
        // On Linux, we must use protocol-specific raw sockets
        let rawTcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Tcp)
        let rawUdpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Udp)
        let rawIcmpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp)

        // Separate buffers per socket
        let tcpBuffer : byte[] = Array.zeroCreate<byte> 65535
        let udpBuffer : byte[] = Array.zeroCreate<byte> 65535
        let icmpBuffer : byte[] = Array.zeroCreate<byte> 65535

        // CancellationTokenSource for blocking receive threads
        let cts = new CancellationTokenSource()

        let shouldDropByIpFilter (buffer: byte[]) (len: int) (proto: string) =
            // Drop if too short for IPv4 header
            if len < 20 then
                Interlocked.Increment(&droppedTooShort) |> ignore
                Logger.logTrace (fun () -> $"ExternalGateway(Linux) {proto}: dropped packet, too short ({len} bytes)")
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

        // Use blocking receive in a dedicated thread for more reliable packet capture on Linux
        let tcpReceiveThread () =
            let remoteEp = IPEndPoint(IPAddress.Any, 0) :> EndPoint
            while running do
                try
                    let mutable ep = remoteEp
                    let n = rawTcpSocket.ReceiveFrom(tcpBuffer, &ep)

                    if n > 0 then
                        Interlocked.Increment(&totalTcpReceived) |> ignore

                        if not (shouldDropByIpFilter tcpBuffer n "TCP") then
                            let packet = Array.zeroCreate<byte> n
                            Buffer.BlockCopy(tcpBuffer, 0, packet, 0, n)
                            Interlocked.Increment(&passedToCallback) |> ignore

                            Logger.logTrace (fun () ->
                                $"HEAVY LOG (Linux) - Received TCP {n} bytes from rawTcpSocket, packet: {(summarizePacket packet)}.")

                            match onPacketCallback with
                            | Some cb -> cb packet
                            | None -> ()

                        logStatsIfDue()
                with
                | :? ObjectDisposedException -> ()
                | :? SocketException as se when isShutdownError se.SocketErrorCode -> ()
                | :? SocketException as se when se.SocketErrorCode = SocketError.TimedOut ->
                    // Timeout is expected, just continue the loop
                    logStatsIfDue()
                | :? SocketException as se ->
                    if running then
                        Interlocked.Increment(&tcpReceiveErrors) |> ignore
                        Logger.logError $"ExternalGateway(Linux) TCP receive error: {se.SocketErrorCode} - {se.Message}"
                    logStatsIfDue()
                | ex ->
                    if running then
                        Interlocked.Increment(&tcpReceiveErrors) |> ignore
                        Logger.logError $"ExternalGateway(Linux) TCP receive exception: {ex.Message}"
                    logStatsIfDue()

        let udpReceiveThread () =
            let remoteEp = IPEndPoint(IPAddress.Any, 0) :> EndPoint
            while running do
                try
                    let mutable ep = remoteEp
                    let n = rawUdpSocket.ReceiveFrom(udpBuffer, &ep)

                    if n > 0 then
                        Interlocked.Increment(&totalUdpReceived) |> ignore

                        if not (shouldDropByIpFilter udpBuffer n "UDP") then
                            let packet = Array.zeroCreate<byte> n
                            Buffer.BlockCopy(udpBuffer, 0, packet, 0, n)
                            Interlocked.Increment(&passedToCallback) |> ignore

                            Logger.logTrace (fun () ->
                                $"HEAVY LOG (Linux) - Received UDP {n} bytes from rawUdpSocket, packet: {(summarizePacket packet)}.")

                            match onPacketCallback with
                            | Some cb -> cb packet
                            | None -> ()

                        logStatsIfDue()
                with
                | :? ObjectDisposedException -> ()
                | :? SocketException as se when isShutdownError se.SocketErrorCode -> ()
                | :? SocketException as se when se.SocketErrorCode = SocketError.TimedOut ->
                    // Timeout is expected, just continue the loop
                    logStatsIfDue()
                | :? SocketException as se ->
                    if running then
                        Interlocked.Increment(&udpReceiveErrors) |> ignore
                        Logger.logError $"ExternalGateway(Linux) UDP receive error: {se.SocketErrorCode} - {se.Message}"
                    logStatsIfDue()
                | ex ->
                    if running then
                        Interlocked.Increment(&udpReceiveErrors) |> ignore
                        Logger.logError $"ExternalGateway(Linux) UDP receive exception: {ex.Message}"
                    logStatsIfDue()

        let icmpReceiveThread () =
            let remoteEp = IPEndPoint(IPAddress.Any, 0) :> EndPoint
            while running do
                try
                    let mutable ep = remoteEp
                    let n = rawIcmpSocket.ReceiveFrom(icmpBuffer, &ep)

                    if n > 0 then
                        Interlocked.Increment(&totalIcmpReceived) |> ignore

                        if not (shouldDropByIpFilter icmpBuffer n "ICMP") then
                            let packet = Array.zeroCreate<byte> n
                            Buffer.BlockCopy(icmpBuffer, 0, packet, 0, n)
                            Interlocked.Increment(&passedToCallback) |> ignore

                            Logger.logTrace (fun () ->
                                $"HEAVY LOG (Linux) - Received ICMP {n} bytes from rawIcmpSocket, packet: {(summarizePacket packet)}.")

                            match onPacketCallback with
                            | Some cb -> cb packet
                            | None -> ()

                        logStatsIfDue()
                with
                | :? ObjectDisposedException -> ()
                | :? SocketException as se when isShutdownError se.SocketErrorCode -> ()
                | :? SocketException as se when se.SocketErrorCode = SocketError.TimedOut ->
                    // Timeout is expected, just continue the loop
                    logStatsIfDue()
                | :? SocketException as se ->
                    if running then
                        Interlocked.Increment(&icmpReceiveErrors) |> ignore
                        Logger.logError $"ExternalGateway(Linux) ICMP receive error: {se.SocketErrorCode} - {se.Message}"
                    logStatsIfDue()
                | ex ->
                    if running then
                        Interlocked.Increment(&icmpReceiveErrors) |> ignore
                        Logger.logError $"ExternalGateway(Linux) ICMP receive exception: {ex.Message}"
                    logStatsIfDue()

        let mutable tcpThread : Thread option = None
        let mutable udpThread : Thread option = None
        let mutable icmpThread : Thread option = None

        do
            // For sending complete IPv4 packets (including IP header)
            rawTcpSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true)
            rawUdpSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true)
            rawIcmpSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true)

            // Bind to the server external IP
            // On Linux, binding raw sockets to a specific IP filters received packets to that destination
            let ep = IPEndPoint(config.serverPublicIp, 0)
            rawTcpSocket.Bind(ep)
            rawUdpSocket.Bind(ep)
            rawIcmpSocket.Bind(ep)

            // Set receive buffer sizes for better performance
            try
                rawTcpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 1024 * 1024)
                rawUdpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 1024 * 1024)
                rawIcmpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 1024 * 1024)
            with _ -> ()

            // Set receive timeout to allow periodic checks of running flag
            rawTcpSocket.ReceiveTimeout <- 1000
            rawUdpSocket.ReceiveTimeout <- 1000
            rawIcmpSocket.ReceiveTimeout <- 1000

            Logger.logInfo $"ExternalGateway(Linux): raw TCP+UDP+ICMP sockets bound to {config.serverPublicIp}"

        member _.start(onPacketFromInternet: byte[] -> unit) =
            if running then
                Logger.logWarn "ExternalGateway(Linux) already running"
            else
                onPacketCallback <- Some onPacketFromInternet
                running <- true
                statsStopwatch.Restart()

                // Start dedicated receive threads
                let tcp = Thread(ThreadStart(tcpReceiveThread), IsBackground = true, Name = "ExternalGateway-TCP")
                let udp = Thread(ThreadStart(udpReceiveThread), IsBackground = true, Name = "ExternalGateway-UDP")
                let icmp = Thread(ThreadStart(icmpReceiveThread), IsBackground = true, Name = "ExternalGateway-ICMP")

                tcpThread <- Some tcp
                udpThread <- Some udp
                icmpThread <- Some icmp

                tcp.Start()
                udp.Start()
                icmp.Start()

                Logger.logInfo "ExternalGateway(Linux) started (raw TCP + raw UDP + raw ICMP with dedicated threads)"

        member _.sendOutbound(packet: byte[]) =
            if packet.Length < 20 then
                Logger.logWarn "ExternalGateway(Linux).sendOutbound: Packet too short (< 20 bytes), dropping"
            else
                match getDestinationIpAddress packet, getProtocolByte packet with
                | Some dstIp, Some proto ->
                    let remoteEndPoint = IPEndPoint(dstIp, 0)

                    try
                        // IPv4 protocol: ICMP=1, TCP=6, UDP=17
                        match proto with
                        | 1uy ->
                            let sent = rawIcmpSocket.SendTo(packet, remoteEndPoint)
                            Logger.logTrace (fun () -> $"HEAVY LOG (Linux) - Sent {sent} bytes to rawIcmpSocket, remoteEndPoint: {remoteEndPoint}, packet: {(summarizePacket packet)}.")
                        | 6uy ->
                            let sent = rawTcpSocket.SendTo(packet, remoteEndPoint)
                            Logger.logTrace (fun () -> $"HEAVY LOG (Linux) - Sent {sent} bytes to rawTcpSocket, remoteEndPoint: {remoteEndPoint}, packet: {(summarizePacket packet)}.")
                        | 17uy ->
                            let sent = rawUdpSocket.SendTo(packet, remoteEndPoint)
                            Logger.logTrace (fun () -> $"HEAVY LOG (Linux) - Sent {sent} bytes to rawUdpSocket, remoteEndPoint: {remoteEndPoint}, packet: {(summarizePacket packet)}.")
                        | _ ->
                            Logger.logTrace (fun () -> $"ExternalGateway(Linux).sendOutbound: unsupported ip proto={proto}, drop. Packet: {summarizePacket packet}")
                    with ex ->
                        if getDstIp4 packet <> "255.255.255.255" then
                            Logger.logError $"ExternalGateway(Linux).sendOutbound failed: {(summarizePacket packet)}, exception: '{ex.Message}'."
                | _ -> Logger.logWarn "ExternalGateway(Linux).sendOutbound: Could not extract dst/proto, dropping packet"

        member _.stop() =
            Logger.logInfo "ExternalGateway(Linux) stopping"
            running <- false
            onPacketCallback <- None

            // Cancel and close sockets to unblock receive threads
            try cts.Cancel() with _ -> ()
            try rawTcpSocket.Close() with _ -> ()
            try rawUdpSocket.Close() with _ -> ()
            try rawIcmpSocket.Close() with _ -> ()

            // Wait for threads to finish
            match tcpThread with
            | Some t -> try t.Join(2000) |> ignore with _ -> ()
            | None -> ()

            match udpThread with
            | Some t -> try t.Join(2000) |> ignore with _ -> ()
            | None -> ()

            match icmpThread with
            | Some t -> try t.Join(2000) |> ignore with _ -> ()
            | None -> ()

            tcpThread <- None
            udpThread <- None
            icmpThread <- None

            try rawTcpSocket.Dispose() with _ -> ()
            try rawUdpSocket.Dispose() with _ -> ()
            try rawIcmpSocket.Dispose() with _ -> ()
            try cts.Dispose() with _ -> ()

            Logger.logInfo "ExternalGateway(Linux) stopped"

        interface IDisposable with
            member this.Dispose() = this.stop()
