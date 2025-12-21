namespace Softellect.Vpn.Client

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Softellect.Sys.Logging
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Core.UdpProtocol

module UdpClient =

    [<Literal>]
    let FragmentsYieldEvery = 32

    /// Response data passed through TCS: (msgType, clientId, logicalPayload).
    type private ResponseData = byte * VpnClientId * byte[]

    /// Record for pending request tracking.
    type private PendingRequest =
        {
            createdAtTicks : int64
            expectedMsgType : byte
            clientId : VpnClientId
            tcs : TaskCompletionSource<ResponseData>
        }


    /// Interface for injecting packets into the tunnel (decouples from Tunnel module).
    type IPacketInjector =
        abstract injectPacket: byte[] -> Result<unit, string>


    /// Push dataplane UDP client.
    /// This client uses push semantics: sends packets immediately to the server,
    /// receives packets pushed from the server, no polling.
    type VpnPushUdpClient(data: VpnClientAccessInfo) =
        let serverIp = data.serverAccessInfo.getIpAddress()
        let serverPort = data.serverAccessInfo.getServicePort().value
        let serverEndpoint = IPEndPoint(serverIp.ipAddress, serverPort)
        let udpClient = new UdpClient()
        let clientCts = new CancellationTokenSource()
        let clientPushStats = ClientPushStats()

        // Bounded queue for outbound packets (from TUN to server).
        let outboundQueue = BoundedPacketQueue(PushQueueMaxBytes, PushQueueMaxPackets)

        // Bounded queue for inbound packets (from server to TUN).
        let inboundPacketQueue = BoundedPacketQueue(PushQueueMaxBytes, PushQueueMaxPackets)

        let mutable sendSeq = 0u
        let mutable packetInjector : IPacketInjector option = None
        let mutable receiveTask : Task option = None
        let mutable sendTask : Task option = None
        let mutable keepaliveTask : Task option = None

        do
            // Bind to an ephemeral local port.
            udpClient.Client.Bind(IPEndPoint(IPAddress.Any, 0))

            // Connect to the server endpoint.
            udpClient.Connect(serverEndpoint)

            // Set receive timeout for periodic checks.
            udpClient.Client.ReceiveTimeout <- CleanupIntervalMs

            Logger.logInfo $"Created - Server: {serverIp}:{serverPort}, ClientId: {data.vpnClientId.value}, Local={udpClient.Client.LocalEndPoint}"

        /// Get next send sequence number.
        let getNextSeq () =
            let seq = sendSeq
            sendSeq <- sendSeq + 1u
            seq

        /// UDP receive loop - receives pushed datagrams from the server.
        let receiveLoop () =
            Logger.logInfo "Receive loop started."
            while not clientCts.Token.IsCancellationRequested do
                try
                    let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                    let data = udpClient.Receive(&remoteEp)

                    clientPushStats.udpRxDatagrams.increment()
                    clientPushStats.udpRxBytes.addInt(data.Length)

                    match tryParsePushHeader data with
                    | Ok (header, payload) ->
                        if header.msgType = PushMsgTypeData && payload.Length > 0 then
                            // Inject directly if injector is available, otherwise queue.
                            match packetInjector with
                            | Some injector ->
                                match injector.injectPacket(payload) with
                                | Ok () -> ()
                                | Error msg ->
                                    clientPushStats.droppedQueueFullInject.increment()
                                    Logger.logWarn $"Push client: Failed to inject packet: {msg}"
                            | None ->
                                // Queue for later injection.
                                if not (inboundPacketQueue.enqueue(payload)) then
                                    clientPushStats.droppedQueueFullInject.increment()
                        elif header.msgType = PushMsgTypeKeepalive then
                            Logger.logTrace (fun () -> "Push client: Received keepalive from server")
                        else
                            Logger.logTrace (fun () -> $"Push client: Unknown msgType 0x{header.msgType:X2}")
                    | Error () ->
                        Logger.logTrace (fun () -> "Push client: Invalid push header received")

                    // Log stats periodically.
                    if clientPushStats.shouldLog() then
                        Logger.logInfo (clientPushStats.getSummary())
                with
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                    // Timeout is expected - allows checking cancellation.
                    if clientPushStats.shouldLog() then
                        Logger.logInfo (clientPushStats.getSummary())
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.Interrupted ->
                    // Socket was closed during shutdown.
                    ()
                | :? ObjectDisposedException -> ()
                | _ when clientCts.Token.IsCancellationRequested -> ()
                | ex -> Logger.logError $"Receive error: {ex.Message}"

            Logger.logInfo "Receive loop stopped."

        /// UDP send loop - sends queued packets to the server.
        let sendLoop () =
            Logger.logInfo "Send loop started."
            while not clientCts.Token.IsCancellationRequested do
                try
                    // Wait for packets with a short timeout.
                    if outboundQueue.wait(10) then
                        // Dequeue and send up to a batch of packets.
                        let mutable hasMore = true

                        while hasMore do
                            match outboundQueue.tryDequeue() with
                            | Some packet ->
                                // Check MTU.
                                if packet.Length > PushMaxPayload then
                                    clientPushStats.droppedMtu.increment()
                                    Logger.logWarn $"Push client: Dropping oversized packet ({packet.Length} > {PushMaxPayload})"
                                else
                                    let seq = getNextSeq()
                                    let datagram = buildPushData data.vpnClientId seq packet

                                    try
                                        udpClient.Send(datagram, datagram.Length) |> ignore
                                        clientPushStats.udpTxDatagrams.increment()
                                        clientPushStats.udpTxBytes.addInt(datagram.Length)
                                    with
                                    | ex -> Logger.logWarn $"Push client: Send failed: {ex.Message}"
                            | None -> hasMore <- false
                with
                | :? ObjectDisposedException -> ()
                | _ when clientCts.Token.IsCancellationRequested -> ()
                | ex -> Logger.logError $"Send error: {ex.Message}"

            Logger.logInfo "Send loop stopped."

        /// Keepalive loop - sends periodic keepalives to maintain NAT mapping.
        let keepaliveLoop () =
            Logger.logInfo "Keepalive loop started."
            while not clientCts.Token.IsCancellationRequested do
                try
                    Thread.Sleep(PushKeepaliveIntervalMs)
                    if not clientCts.Token.IsCancellationRequested then
                        let seq = getNextSeq()
                        let datagram = buildPushKeepalive data.vpnClientId seq

                        try
                            udpClient.Send(datagram, datagram.Length) |> ignore
                            Logger.logTrace (fun () -> $"Push client: Sent keepalive seq={seq}")
                        with
                        | ex -> Logger.logWarn $"Push client: Keepalive send failed: {ex.Message}"
                with
                | :? ObjectDisposedException -> ()
                | _ when clientCts.Token.IsCancellationRequested -> ()
                | ex -> Logger.logError $"Keepalive error: {ex.Message}"

            Logger.logInfo "Keepalive loop stopped."

        /// Start the push dataplane loops.
        member _.start() =
            receiveTask <- Some (Task.Run(receiveLoop))
            sendTask <- Some (Task.Run(sendLoop))
            keepaliveTask <- Some (Task.Run(keepaliveLoop))
            Logger.logInfo "Started"

        /// Stop the push dataplane loops.
        member _.stop() =
            clientCts.Cancel()

            let waitTask (t: Task option) =
                match t with
                | Some task -> try task.Wait(TimeSpan.FromSeconds(2.0)) |> ignore with | _ -> ()
                | None -> ()

            waitTask receiveTask
            waitTask sendTask
            waitTask keepaliveTask

            receiveTask <- None
            sendTask <- None
            keepaliveTask <- None

            Logger.logInfo "Stopped"

        /// Set the packet injector for direct injection into the tunnel.
        member _.setPacketInjector(injector: IPacketInjector) =
            packetInjector <- Some injector

        /// Enqueue a packet for sending to the server.
        /// Returns true if enqueued, false if queue rejected (too large).
        member _.enqueueOutbound(packet: byte[]) : bool =
            clientPushStats.tunRxPackets.increment()
            clientPushStats.tunRxBytes.addInt(packet.Length)
            if outboundQueue.enqueue(packet) then
                true
            else
                clientPushStats.droppedQueueFullOutbound.increment()
                false

        /// Try to dequeue a received packet (for when the injector is not set).
        member _.tryDequeueInbound() : byte[] option =
            inboundPacketQueue.tryDequeue()

        /// Get the inbound queue for waiting.
        member _.inboundQueue = inboundPacketQueue

        /// Get stats.
        member _.stats = clientPushStats

        /// Get client ID.
        member _.clientId = data.vpnClientId

        interface IDisposable with
            member this.Dispose() =
                this.stop()
                udpClient.Close()
                udpClient.Dispose()
                clientCts.Dispose()


    let createVpnPushUdpClient (clientAccessInfo: VpnClientAccessInfo) =
        new VpnPushUdpClient(clientAccessInfo)
