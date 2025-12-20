namespace Softellect.Vpn.Client

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Softellect.Sys.Core
open Softellect.Sys.Logging
open Softellect.Wcf.Common
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
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

    /// Reassembly key for the client: (msgType, clientId, requestId).
    type private ClientReassemblyKey = byte * VpnClientId * uint32


    type VpnUdpClient(data: VpnClientAccessInfo) =
        let clientId = data.vpnClientId
        let serverIp = data.serverAccessInfo.getIpAddress()
        let serverPort = data.serverAccessInfo.getServicePort().value
        let serverEndpoint = IPEndPoint(serverIp.ipAddress, serverPort)

        let udpClient = new System.Net.Sockets.UdpClient()
        let clientCts = new CancellationTokenSource()
        let pendingRequests = ConcurrentDictionary<uint32, PendingRequest>()
        let reassemblyMap = ConcurrentDictionary<ClientReassemblyKey, ReassemblyState>()
        let mutable nextRequestIdInt = 0

        let timeoutTicks = int64 RequestTimeoutMs * Stopwatch.Frequency / 1000L

        do
            // Bind to an ephemeral local port so Receive() works.
            udpClient.Client.Bind(IPEndPoint(IPAddress.Any, 0))

            // Connect fixes the remote endpoint and enables Receive() on this socket.
            udpClient.Connect(serverEndpoint)

            // Keep timeout so the loop can check cancellation periodically.
            udpClient.Client.ReceiveTimeout <- CleanupIntervalMs

            Logger.logInfo $"VpnUdpClient created - Server: {serverIp}:{serverPort}, ClientId: {clientId.value}, Local={udpClient.Client.LocalEndPoint}"

        /// Receive loop that reads all incoming datagrams and dispatches to pending requests.
        let receiveLoop () =
            Logger.logTrace (fun () -> "VpnUdpClient receive loop started.")
            while not clientCts.Token.IsCancellationRequested do
                try
                    let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                    let data = udpClient.Receive(&remoteEp)

                    match tryParseFragmentHeader data with
                    | Ok (msgType, respClientId, requestId, fragIndex, fragCount, fragmentPayload) ->
                        let key : ClientReassemblyKey = (msgType, respClientId, requestId)

                        let completeRequest (logicalPayload: byte[]) =
                            match pendingRequests.TryRemove(requestId) with
                            | true, pending ->
                                let responseData : ResponseData = (msgType, respClientId, logicalPayload)
                                pending.tcs.TrySetResult(responseData) |> ignore
                            | false, _ ->
                                Logger.logTrace (fun () -> $"Dropped response with unknown requestId: {requestId}, msgType: 0x{msgType:X2}")

                        if fragCount = 1us && fragIndex = 0us then
                            // Single fragment - complete immediately.
                            completeRequest fragmentPayload
                        else
                            // Multi-fragment - reassemble.
                            let nowTicks = Stopwatch.GetTimestamp()
                            let state = reassemblyMap.GetOrAdd(key, fun _ -> createReassemblyState nowTicks fragCount)

                            // Validate fragCount matches.
                            if state.fragCount <> fragCount then
                                Logger.logTrace (fun () -> $"Fragment count mismatch for requestId {requestId}: expected {state.fragCount}, got {fragCount}")
                            else
                                match tryAddFragment state fragIndex fragmentPayload with
                                | Some logicalPayload ->
                                    // Reassembly complete - remove from map and complete pending request.
                                    reassemblyMap.TryRemove(key) |> ignore
                                    completeRequest logicalPayload
                                | None ->
                                    // More fragments needed.
                                    ()
                    | Error () ->
                        // Invalid header - drop silently.
                        Logger.logTrace (fun () -> "Dropped response with invalid header.")
                with
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                    // Timeout is expected - allows checking cancellation and cleanup.
                    ()
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.Interrupted ->
                    // Socket was closed during shutdown.
                    ()
                | :? ObjectDisposedException ->
                    // Socket disposed during shutdown.
                    ()
                | ex when clientCts.Token.IsCancellationRequested ->
                    // Shutting down.
                    ()
                | ex ->
                    Logger.logError $"VpnUdpClient receive error: {ex.Message}"

            Logger.logTrace (fun () -> "VpnUdpClient receive loop stopped.")

        /// Cleanup loop that times out pending requests and stale reassemblies.
        let cleanupLoop () =
            Logger.logTrace (fun () -> "VpnUdpClient cleanup loop started.")
            while not clientCts.Token.IsCancellationRequested do
                try
                    Thread.Sleep(CleanupIntervalMs)
                    let nowTicks = Stopwatch.GetTimestamp()

                    // Cleanup pending requests.
                    for kvp in pendingRequests do
                        let requestId = kvp.Key
                        let pending = kvp.Value
                        if nowTicks - pending.createdAtTicks > timeoutTicks then
                            match pendingRequests.TryRemove(requestId) with
                            | true, p ->
                                p.tcs.TrySetException(TimeoutException("Request timed out")) |> ignore
                                Logger.logTrace (fun () -> $"Timed out requestId: {requestId}")
                            | false, _ -> ()

                    // Cleanup stale reassemblies (using same timeout as requests).
                    for kvp in reassemblyMap do
                        let key = kvp.Key
                        let state = kvp.Value
                        if nowTicks - state.createdAtTicks > timeoutTicks then
                            reassemblyMap.TryRemove(key) |> ignore
                            let (m, _, _) = key
                            Logger.logTrace (fun () -> $"Timed out reassembly: msgType=0x{m:X2}, received {state.receivedCount}/{state.fragCount}")
                with
                | :? ObjectDisposedException -> ()
                | ex when clientCts.Token.IsCancellationRequested -> ()
                | ex ->
                    Logger.logError $"VpnUdpClient cleanup error: {ex.Message}"

            Logger.logTrace (fun () -> "VpnUdpClient cleanup loop stopped.")

        // Start background loops.
        do
            Task.Run(receiveLoop) |> ignore
            Task.Run(cleanupLoop) |> ignore

        /// Allocate a new requestId.
        let allocateRequestId () : uint32 =
            uint32 (Interlocked.Increment(&nextRequestIdInt))

        /// Async send a request and wait for the response without blocking.
        let sendRequestAsync (requestMsgType: byte) (reqClientId: VpnClientId) (payload: byte[]) : Task<Result<ResponseData, VpnError>> =
            task {
                let requestId = allocateRequestId()
                let expectedMsgType = expectedResponseType requestMsgType
                let tcs = TaskCompletionSource<ResponseData>(TaskCreationOptions.RunContinuationsAsynchronously)

                let pending =
                    {
                        createdAtTicks = Stopwatch.GetTimestamp()
                        expectedMsgType = expectedMsgType
                        clientId = reqClientId
                        tcs = tcs
                    }

                pendingRequests.[requestId] <- pending

                try
                    // Build and send all fragments with pacing.
                    let fragments = buildFragments requestMsgType reqClientId requestId payload
                    let mutable fragmentCount = 0
                    for fragment in fragments do
                        udpClient.Send(fragment, fragment.Length) |> ignore
                        fragmentCount <- fragmentCount + 1
                        if fragmentCount % FragmentsYieldEvery = 0 then
                            Thread.Yield() |> ignore

                    // Wait for completion with a hard stop timeout without blocking.
                    let hardTimeoutMs = RequestTimeoutMs + CleanupIntervalMs
                    let! completed = Task.WhenAny(tcs.Task, Task.Delay(hardTimeoutMs, clientCts.Token))

                    if Object.ReferenceEquals(completed, tcs.Task) then
                        return Ok tcs.Task.Result
                    else
                        // Hard timeout - remove pending and return error.
                        pendingRequests.TryRemove(requestId) |> ignore
                        return Error (ConnectionErr ConnectionTimeoutErr)
                with
                | :? AggregateException as ae when (ae.InnerException :? TimeoutException) ->
                    return Error (ConnectionErr ConnectionTimeoutErr)
                | :? TimeoutException ->
                    return Error (ConnectionErr ConnectionTimeoutErr)
                | :? TaskCanceledException ->
                    pendingRequests.TryRemove(requestId) |> ignore
                    return Error (ConnectionErr ConnectionTimeoutErr)
                | :? SocketException as ex ->
                    pendingRequests.TryRemove(requestId) |> ignore
                    return Error (ConnectionErr (ServerUnreachableErr ex.Message))
                | ex ->
                    pendingRequests.TryRemove(requestId) |> ignore
                    return Error (ConnectionErr (ServerUnreachableErr ex.Message))
            }

        /// Send a request and wait for the response (synchronous wrapper).
        let sendRequest (requestMsgType: byte) (reqClientId: VpnClientId) (payload: byte[]) : Result<ResponseData, VpnError> =
            (sendRequestAsync requestMsgType reqClientId payload).GetAwaiter().GetResult()

        /// Parse and validate the response from ResponseData.
        let parseResponse (expectedMsgType: byte) (expectedClientId: VpnClientId) (responseData: ResponseData) : Result<byte[], VpnError> =
            let (msgType, responseClientId, payload) = responseData
            if msgType = MsgTypeErrorResponse then
                let errorMsg =
                    if payload.Length > 0 then
                        let len = min payload.Length 1024
                        System.Text.Encoding.UTF8.GetString(payload, 0, len)
                    else
                        "Unknown error"
                Error (ConfigErr errorMsg)
            elif msgType <> expectedMsgType then
                Error (ConfigErr $"Unexpected message type: expected 0x{expectedMsgType:X2}, got 0x{msgType:X2}")
            elif responseClientId.value <> expectedClientId.value then
                Error (ConfigErr "Client ID mismatch in response")
            else
                Ok payload

        interface IVpnClient with
            member _.authenticate request =
                Logger.logTrace (fun () -> $"authenticate: Sending auth request for client {request.clientId.value}")

                match trySerialize wcfSerializationFormat request with
                | Ok payload ->
                    match sendRequest MsgTypeAuthenticate request.clientId payload with
                    | Ok responseData ->
                        match parseResponse MsgTypeAuthenticateResponse request.clientId responseData with
                        | Ok responsePayload ->
                            match tryDeserialize<VpnAuthResult> wcfSerializationFormat responsePayload with
                            | Ok result -> result
                            | Error e -> Error (ConfigErr $"Deserialization error: {e}")
                        | Error e -> Error e
                    | Error e -> Error e
                | Error e ->
                    Error (ConfigErr $"Serialization error: {e}")

            member _.sendPackets packets =
                Logger.logTrace (fun () -> $"Sending {packets.Length} packets for client {clientId.value}.")

                match trySerialize wcfSerializationFormat packets with
                | Ok payload ->
                    match sendRequest MsgTypeSendPackets clientId payload with
                    | Ok responseData ->
                        match parseResponse MsgTypeSendPacketsResponse clientId responseData with
                        | Ok responsePayload ->
                            match tryDeserialize<VpnUnitResult> wcfSerializationFormat responsePayload with
                            | Ok result ->
                                Logger.logTrace (fun () -> $"Result: '%A{result}'.")
                                result
                            | Error e -> Error (ConfigErr $"Deserialization error: {e}")
                        | Error e -> Error e
                    | Error e -> Error e
                | Error e ->
                    Error (ConfigErr $"Serialization error: {e}")

            member _.receivePackets reqClientId =
                Logger.logTrace (fun () -> $"receivePackets: Receiving packets for client {reqClientId.value}")

                // Build the long-poll request payload.
                let receiveRequest = { maxWaitMs = DefaultMaxWaitMs; maxPackets = DefaultMaxPackets }

                match trySerialize wcfSerializationFormat receiveRequest with
                | Ok payload ->
                    let result =
                        match sendRequest MsgTypeReceivePackets reqClientId payload with
                        | Ok responseData ->
                            match parseResponse MsgTypeReceivePacketsResponse reqClientId responseData with
                            | Ok responsePayload ->
                                match tryDeserialize<VpnPacketsResult> wcfSerializationFormat responsePayload with
                                | Ok result -> result
                                | Error e -> Error (ConfigErr $"Deserialization error: {e}")
                            | Error e -> Error e
                        | Error e -> Error e

                    match result with
                    | Ok (Some r) -> Logger.logTrace (fun () -> $"Received {r.Length} packets for client {reqClientId.value}")
                    | Ok None -> Logger.logTrace (fun () -> "Empty response.")
                    | Error e -> Logger.logWarn $"ERROR: '{e}'."

                    result
                | Error e ->
                    Error (ConfigErr $"Serialization error: {e}")

        interface IDisposable with
            member _.Dispose() =
                clientCts.Cancel()
                udpClient.Close()
                udpClient.Dispose()
                clientCts.Dispose()


    let createVpnUdpClient (clientAccessInfo: VpnClientAccessInfo) : IVpnClient =
        new VpnUdpClient(clientAccessInfo) :> IVpnClient


    // ==========================================================================
    // PUSH DATAPLANE CLIENT (spec 037)
    // ==========================================================================

    /// Interface for injecting packets into the tunnel (decouples from Tunnel module).
    type IPacketInjector =
        abstract InjectPacket : byte[] -> Result<unit, string>


    /// Push dataplane UDP client.
    /// This client uses push semantics: sends packets immediately to the server,
    /// receives packets pushed from the server, no polling.
    type VpnPushUdpClient(data: VpnClientAccessInfo) =
        // let clientId = data.vpnClientId
        let serverIp = data.serverAccessInfo.getIpAddress()
        let serverPort = data.serverAccessInfo.getServicePort().value
        let serverEndpoint = IPEndPoint(serverIp.ipAddress, serverPort)

        let udpClient = new System.Net.Sockets.UdpClient()
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

        /// UDP receive loop - receives pushed datagrams from server.
        let receiveLoop () =
            Logger.logInfo "Receive loop started."
            while not clientCts.Token.IsCancellationRequested do
                try
                    let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                    let data = udpClient.Receive(&remoteEp)

                    clientPushStats.UdpRxDatagrams.Increment()
                    clientPushStats.UdpRxBytes.AddInt(data.Length)

                    match tryParsePushHeader data with
                    | Ok (header, payload) ->
                        if header.msgType = PushMsgTypeData && payload.Length > 0 then
                            // Inject directly if injector is available, otherwise queue.
                            match packetInjector with
                            | Some injector ->
                                match injector.InjectPacket(payload) with
                                | Ok () -> ()
                                | Error msg ->
                                    clientPushStats.DroppedQueueFullInject.Increment()
                                    Logger.logWarn $"Push client: Failed to inject packet: {msg}"
                            | None ->
                                // Queue for later injection.
                                if not (inboundPacketQueue.Enqueue(payload)) then
                                    clientPushStats.DroppedQueueFullInject.Increment()
                        elif header.msgType = PushMsgTypeKeepalive then
                            Logger.logTrace (fun () -> "Push client: Received keepalive from server")
                        else
                            Logger.logTrace (fun () -> $"Push client: Unknown msgType 0x{header.msgType:X2}")
                    | Error () ->
                        Logger.logTrace (fun () -> "Push client: Invalid push header received")

                    // Log stats periodically.
                    if clientPushStats.ShouldLog() then
                        Logger.logInfo (clientPushStats.GetSummary())
                with
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                    // Timeout is expected - allows checking cancellation.
                    if clientPushStats.ShouldLog() then
                        Logger.logInfo (clientPushStats.GetSummary())
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.Interrupted ->
                    // Socket was closed during shutdown.
                    ()
                | :? ObjectDisposedException -> ()
                | ex when clientCts.Token.IsCancellationRequested -> ()
                | ex ->
                    Logger.logError $"Receive error: {ex.Message}"

            Logger.logInfo "Receive loop stopped."

        /// UDP send loop - sends queued packets to server.
        let sendLoop () =
            Logger.logInfo "Send loop started."
            while not clientCts.Token.IsCancellationRequested do
                try
                    // Wait for packets with a short timeout.
                    if outboundQueue.Wait(10) then
                        // Dequeue and send up to a batch of packets.
                        let mutable hasMore = true
                        while hasMore do
                            match outboundQueue.TryDequeue() with
                            | Some packet ->
                                // Check MTU.
                                if packet.Length > PushMaxPayload then
                                    clientPushStats.DroppedMtu.Increment()
                                    Logger.logWarn $"Push client: Dropping oversized packet ({packet.Length} > {PushMaxPayload})"
                                else
                                    let seq = getNextSeq()
                                    let datagram = buildPushData data.vpnClientId seq packet

                                    try
                                        udpClient.Send(datagram, datagram.Length) |> ignore
                                        clientPushStats.UdpTxDatagrams.Increment()
                                        clientPushStats.UdpTxBytes.AddInt(datagram.Length)
                                    with
                                    | ex ->
                                        Logger.logWarn $"Push client: Send failed: {ex.Message}"
                            | None ->
                                hasMore <- false
                with
                | :? ObjectDisposedException -> ()
                | ex when clientCts.Token.IsCancellationRequested -> ()
                | ex ->
                    Logger.logError $"Send error: {ex.Message}"

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
                        | ex ->
                            Logger.logWarn $"Push client: Keepalive send failed: {ex.Message}"
                with
                | :? ObjectDisposedException -> ()
                | ex when clientCts.Token.IsCancellationRequested -> ()
                | ex ->
                    Logger.logError $"Keepalive error: {ex.Message}"

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

        /// Set the packet injector for direct injection into tunnel.
        member _.setPacketInjector(injector: IPacketInjector) =
            packetInjector <- Some injector

        /// Enqueue a packet for sending to server.
        /// Returns true if enqueued, false if queue rejected (too large).
        member _.enqueueOutbound(packet: byte[]) : bool =
            clientPushStats.TunRxPackets.Increment()
            clientPushStats.TunRxBytes.AddInt(packet.Length)
            if outboundQueue.Enqueue(packet) then
                true
            else
                clientPushStats.DroppedQueueFullOutbound.Increment()
                false

        /// Try to dequeue a received packet (for when the injector is not set).
        member _.tryDequeueInbound() : byte[] option =
            inboundPacketQueue.TryDequeue()

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


    let createVpnPushUdpClient (clientAccessInfo: VpnClientAccessInfo) : VpnPushUdpClient =
        new VpnPushUdpClient(clientAccessInfo)
