namespace Softellect.Vpn.Server

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Softellect.Sys.Core
open Softellect.Sys.Logging
open Softellect.Sys.ExitErrorCodes
open Softellect.Wcf.Common
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Core.UdpProtocol
open Softellect.Vpn.Server.Service
open Softellect.Vpn.Server.ClientRegistry

module UdpServer =

    /// Reassembly key for server: (msgType, clientId, requestId).
    type private ServerReassemblyKey = byte * VpnClientId * uint32

    /// Work item for the bounded channel.
    type private WorkItem = byte * VpnClientId * uint32 * byte[] * IPEndPoint

    /// Number of worker tasks.
    let private workerCount = 16

    type VpnUdpHostedService(data: VpnServerData, service: IVpnService) =
        let serverPort = data.serverAccessInfo.serviceAccessInfo.getServicePort().value
        let endpointMap = ConcurrentDictionary<VpnClientId, IPEndPoint>()
        let reassemblyMap = ConcurrentDictionary<ServerReassemblyKey, ReassemblyState>()
        let mutable udpClient : System.Net.Sockets.UdpClient option = None
        let mutable cancellationTokenSource : CancellationTokenSource option = None

        let reassemblyTimeoutTicks = int64 ServerReassemblyTimeoutMs * Stopwatch.Frequency / 1000L

        // Bounded channel for work items.
        let channelOptions = BoundedChannelOptions(4096, FullMode = BoundedChannelFullMode.Wait, SingleWriter = true, SingleReader = false)
        let workChannel = Channel.CreateBounded<WorkItem>(channelOptions)

        let processAuthenticate (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[][] =
            Logger.logTrace (fun () -> $"processAuthenticate: clientId={clientId.value}, requestId={requestId}, payloadLen={payload.Length}")
            match tryDeserialize<VpnAuthRequest> wcfSerializationFormat payload with
            | Ok request ->
                let result = service.authenticate request
                match trySerialize wcfSerializationFormat result with
                | Ok responsePayload -> buildFragments MsgTypeAuthenticateResponse clientId requestId responsePayload
                | Error _ -> buildErrorResponseFragments clientId requestId "Serialization error"
            | Error _ ->
                buildErrorResponseFragments clientId requestId "Invalid authenticate payload"

        let processSendPackets (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[][] =
            Logger.logTrace (fun () -> $"processSendPackets: clientId={clientId.value}, requestId={requestId}, payloadLen={payload.Length}")
            match tryDeserialize<byte[][]> wcfSerializationFormat payload with
            | Ok packets ->
                let result = service.sendPackets (clientId, packets)
                match trySerialize wcfSerializationFormat result with
                | Ok responsePayload -> buildFragments MsgTypeSendPacketsResponse clientId requestId responsePayload
                | Error _ -> buildErrorResponseFragments clientId requestId "Serialization error"
            | Error _ ->
                buildErrorResponseFragments clientId requestId "Invalid sendPackets payload"

        let processReceivePackets (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[][] =
            Logger.logTrace (fun () -> $"processReceivePackets: clientId={clientId.value}, requestId={requestId}, payloadLen={payload.Length}")

            // Deserialize long-poll request parameters.
            let receiveRequest =
                match tryDeserialize<ReceivePacketsRequest> wcfSerializationFormat payload with
                | Ok req -> req
                | Error _ -> { maxWaitMs = DefaultMaxWaitMs; maxPackets = DefaultMaxPackets }

            // Use wait-aware service if available.
            let result =
                match service with
                | :? IVpnServiceInternal as internalService ->
                    internalService.receivePacketsWithWait(clientId, receiveRequest.maxWaitMs, receiveRequest.maxPackets)
                | _ ->
                    service.receivePackets clientId

            match trySerialize wcfSerializationFormat result with
            | Ok responsePayload -> buildFragments MsgTypeReceivePacketsResponse clientId requestId responsePayload
            | Error _ -> buildErrorResponseFragments clientId requestId "Serialization error"

        let processRequest (msgType: byte) (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[][] =
            match msgType with
            | 0x01uy -> processAuthenticate clientId requestId payload
            | 0x02uy -> processSendPackets clientId requestId payload
            | 0x03uy -> processReceivePackets clientId requestId payload
            | _ -> buildErrorResponseFragments clientId requestId $"Unknown message type: 0x{msgType:X2}"

        /// Cleanup stale reassemblies.
        let cleanupReassemblies () =
            let nowTicks = Stopwatch.GetTimestamp()
            for kvp in reassemblyMap do
                let key = kvp.Key
                let state = kvp.Value
                if nowTicks - state.createdAtTicks > reassemblyTimeoutTicks then
                    reassemblyMap.TryRemove(key) |> ignore
                    let (m, _, _) = key
                    Logger.logTrace (fun () -> $"Server: Timed out reassembly: msgType=0x{m:X2}, received {state.receivedCount}/{state.fragCount}")

        /// Worker loop that reads from the bounded channel and processes requests.
        let workerLoop (client: System.Net.Sockets.UdpClient) (workerId: int) (ct: CancellationToken) =
            task {
                Logger.logTrace (fun () -> $"UDP server worker {workerId} started")

                while not ct.IsCancellationRequested do
                    try
                        let! workItem = workChannel.Reader.ReadAsync(ct).AsTask()
                        let (msgType, clientId, requestId, logicalPayload, remoteEp) = workItem

                        try
                            Logger.logTrace (fun () -> $"Worker {workerId}: Processing msgType=0x{msgType:X2}, clientId={clientId.value}, requestId={requestId}, payloadLen={logicalPayload.Length}, remoteEp={remoteEp}")

                            // Update endpoint mapping.
                            endpointMap[clientId] <- remoteEp

                            // Process request and send all response fragments.
                            let responseFragments = processRequest msgType clientId requestId logicalPayload

                            for fragment in responseFragments do
                                client.Send(fragment, fragment.Length, remoteEp) |> ignore

                            Logger.logTrace (fun () -> $"Worker {workerId}: Sent msgType=0x{responseFragments[0].[0]:X2}, requestId={requestId}, fragments={responseFragments.Length}")
                        with
                        | ex when ct.IsCancellationRequested -> ()
                        | ex -> Logger.logError $"Worker {workerId} error for requestId {requestId}: {ex.Message}"
                    with
                    | :? OperationCanceledException -> ()
                    | :? ChannelClosedException -> ()
                    | ex when ct.IsCancellationRequested -> ()
                    | ex -> Logger.logError $"Worker {workerId} read error: {ex.Message}"

                Logger.logTrace (fun () -> $"UDP server worker {workerId} stopped")
            } :> Task

        let receiveLoop (client: System.Net.Sockets.UdpClient) (ct: CancellationToken) =
            task {
                Logger.logInfo $"UDP server receive loop started on port {serverPort}"

                while not ct.IsCancellationRequested do
                    try
                        let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                        let data = client.Receive(&remoteEp)

                        match tryParseFragmentHeader data with
                        | Ok (msgType, clientId, requestId, fragIndex, fragCount, fragmentPayload) ->
                            let key : ServerReassemblyKey = (msgType, clientId, requestId)

                            // Capture remoteEp for this request (avoid reusing mutable variable).
                            let capturedRemoteEp = IPEndPoint(remoteEp.Address, remoteEp.Port)

                            if fragCount = 1us && fragIndex = 0us then
                                // Single fragment - enqueue to work channel.
                                let workItem : WorkItem = (msgType, clientId, requestId, fragmentPayload, capturedRemoteEp)
                                do! workChannel.Writer.WriteAsync(workItem, ct).AsTask()
                            else
                                // Multi-fragment - reassemble.
                                let nowTicks = Stopwatch.GetTimestamp()
                                let state = reassemblyMap.GetOrAdd(key, fun _ -> createReassemblyState nowTicks fragCount)

                                // Validate fragCount matches.
                                if state.fragCount <> fragCount then
                                    Logger.logTrace (fun () -> $"Server: Fragment count mismatch for requestId {requestId}: expected {state.fragCount}, got {fragCount}")
                                else
                                    match tryAddFragment state fragIndex fragmentPayload with
                                    | Some logicalPayload ->
                                        // Reassembly complete - remove from map and enqueue to work channel.
                                        reassemblyMap.TryRemove(key) |> ignore
                                        let workItem : WorkItem = (msgType, clientId, requestId, logicalPayload, capturedRemoteEp)
                                        do! workChannel.Writer.WriteAsync(workItem, ct).AsTask()
                                    | None ->
                                        // More fragments needed.
                                        ()
                        | Error () ->
                            // Invalid header - drop silently.
                            Logger.logTrace (fun () -> $"Dropped packet with invalid header from {remoteEp}")

                        // Periodically cleanup stale reassemblies (piggyback on receive).
                        cleanupReassemblies ()
                    with
                    | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                        // Timeout is expected - allows checking cancellation.
                        // Also cleanup stale reassemblies on timeout.
                        cleanupReassemblies ()
                    | :? SocketException as ex when ex.SocketErrorCode = SocketError.Interrupted ->
                        // Socket was closed during shutdown.
                        ()
                    | :? SocketException as ex when ex.SocketErrorCode = SocketError.MessageSize ->
                        // Inbound datagram too large - verify client fragmentation.
                        Logger.logWarn $"MessageSize: inbound datagram too large — verify client fragmentation; dropping."
                    | :? OperationCanceledException -> ()
                    | ex when ct.IsCancellationRequested ->
                        // Shutting down.
                        ()
                    | ex ->
                        Logger.logError $"UDP receive error: {ex.Message}"

                Logger.logInfo "UDP server receive loop stopped"
            } :> Task

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                Logger.logInfo $"Starting UDP server on port {serverPort}"

                let cts = new CancellationTokenSource()
                cancellationTokenSource <- Some cts

                let client = new System.Net.Sockets.UdpClient(serverPort)
                client.Client.ReceiveTimeout <- ServerReceiveTimeoutMs
                udpClient <- Some client

                // Start worker tasks.
                for i = 0 to workerCount - 1 do
                    Task.Run(fun () -> workerLoop client i cts.Token) |> ignore

                // Start receive loop on background thread.
                Task.Run(fun () -> receiveLoop client cts.Token) |> ignore

                Task.CompletedTask

            member _.StopAsync(cancellationToken: CancellationToken) =
                Logger.logInfo "Stopping UDP server"

                match cancellationTokenSource with
                | Some cts ->
                    cts.Cancel()
                    cts.Dispose()
                    cancellationTokenSource <- None
                | None -> ()

                match udpClient with
                | Some client ->
                    client.Close()
                    client.Dispose()
                    udpClient <- None
                | None -> ()

                Task.CompletedTask


    let getUdpHostedService (data: VpnServerData) (service: IVpnService) : IHostedService =
        VpnUdpHostedService(data, service) :> IHostedService


    let getUdpProgram (data: VpnServerData) (getService: unit -> IVpnService) (argv: string[]) =
        fun () ->
            try
                let service = getService()

                let host =
                    Host.CreateDefaultBuilder(argv)
                        .ConfigureServices(fun services ->
                            // Register the IVpnService instance.
                            services.AddSingleton<IVpnService>(service) |> ignore

                            // Register IVpnService as IHostedService (it implements both).
                            services.AddSingleton<IHostedService>(service :> IHostedService) |> ignore

                            // Register the UDP-hosted service.
                            let udpHostedService = getUdpHostedService data service
                            services.AddSingleton<IHostedService>(udpHostedService) |> ignore
                        )
                        .Build()

                Logger.logInfo $"UDP VPN Server starting with subnet: {data.serverAccessInfo.vpnSubnet.value}"
                host.Run()
                CompletedSuccessfully
            with
            | ex ->
                Logger.logCrit $"UDP VPN Server failed: {ex.Message}"
                CriticalError


    // ==========================================================================
    // PUSH DATAPLANE SERVER (spec 037)
    // ==========================================================================

    /// Combined UDP server that handles both legacy (request/response) and push dataplane protocols.
    /// Distinguishes between protocols by checking the magic number at the start of each datagram.
    type VpnCombinedUdpHostedService(data: VpnServerData, service: IVpnPushService, registry: ClientRegistry) =
    // type VpnCombinedUdpHostedService(data: VpnServerData, registry: ClientRegistry) =
        do Logger.logInfo $"Using registry: {registry.GetHashCode()}."

        let serverPort = data.serverAccessInfo.serviceAccessInfo.getServicePort().value
        let endpointMap = ConcurrentDictionary<VpnClientId, IPEndPoint>()
        let reassemblyMap = ConcurrentDictionary<ServerReassemblyKey, ReassemblyState>()
        let pushStats = ServerPushStats()
        let mutable udpClient : System.Net.Sockets.UdpClient option = None
        let mutable cancellationTokenSource : CancellationTokenSource option = None
        let reassemblyTimeoutTicks = int64 ServerReassemblyTimeoutMs * Stopwatch.Frequency / 1000L

        // Bounded channel for legacy work items.
        let channelOptions = BoundedChannelOptions(4096, FullMode = BoundedChannelFullMode.Wait, SingleWriter = true, SingleReader = false)
        let workChannel = Channel.CreateBounded<WorkItem>(channelOptions)

        // ===== Legacy protocol handlers (same as VpnUdpHostedService) =====
        //
        // let processAuthenticate (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[][] =
        //     Logger.logTrace (fun () -> $"processAuthenticate: clientId={clientId.value}, requestId={requestId}, payloadLen={payload.Length}")
        //     match tryDeserialize<VpnAuthRequest> wcfSerializationFormat payload with
        //     | Ok request ->
        //         let result = service.authenticate request
        //         match trySerialize wcfSerializationFormat result with
        //         | Ok responsePayload -> buildFragments MsgTypeAuthenticateResponse clientId requestId responsePayload
        //         | Error _ -> buildErrorResponseFragments clientId requestId "Serialization error"
        //     | Error _ ->
        //         buildErrorResponseFragments clientId requestId "Invalid authenticate payload"
        //
        // let processSendPackets (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[][] =
        //     Logger.logTrace (fun () -> $"processSendPackets: clientId={clientId.value}, requestId={requestId}, payloadLen={payload.Length}")
        //     match tryDeserialize<byte[][]> wcfSerializationFormat payload with
        //     | Ok packets ->
        //         let result = service.sendPackets (clientId, packets)
        //         match trySerialize wcfSerializationFormat result with
        //         | Ok responsePayload -> buildFragments MsgTypeSendPacketsResponse clientId requestId responsePayload
        //         | Error _ -> buildErrorResponseFragments clientId requestId "Serialization error"
        //     | Error _ ->
        //         buildErrorResponseFragments clientId requestId "Invalid sendPackets payload"
        //
        // let processReceivePackets (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[][] =
        //     Logger.logTrace (fun () -> $"processReceivePackets: clientId={clientId.value}, requestId={requestId}, payloadLen={payload.Length}")
        //
        //     let receiveRequest =
        //         match tryDeserialize<ReceivePacketsRequest> wcfSerializationFormat payload with
        //         | Ok req -> req
        //         | Error _ -> { maxWaitMs = DefaultMaxWaitMs; maxPackets = DefaultMaxPackets }
        //
        //     let result =
        //         match service with
        //         | :? IVpnServiceInternal as internalService ->
        //             internalService.receivePacketsWithWait(clientId, receiveRequest.maxWaitMs, receiveRequest.maxPackets)
        //         | _ ->
        //             service.receivePackets clientId
        //
        //     match trySerialize wcfSerializationFormat result with
        //     | Ok responsePayload -> buildFragments MsgTypeReceivePacketsResponse clientId requestId responsePayload
        //     | Error _ -> buildErrorResponseFragments clientId requestId "Serialization error"
        //
        // let processLegacyRequest (msgType: byte) (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[][] =
        //     match msgType with
        //     | 0x01uy -> processAuthenticate clientId requestId payload
        //     | 0x02uy -> processSendPackets clientId requestId payload
        //     | 0x03uy -> processReceivePackets clientId requestId payload
        //     | _ -> buildErrorResponseFragments clientId requestId $"Unknown message type: 0x{msgType:X2}"

        let cleanupReassemblies () =
            let nowTicks = Stopwatch.GetTimestamp()
            for kvp in reassemblyMap do
                let key = kvp.Key
                let state = kvp.Value
                if nowTicks - state.createdAtTicks > reassemblyTimeoutTicks then
                    reassemblyMap.TryRemove(key) |> ignore
                    let (m, _, _) = key
                    Logger.logTrace (fun () -> $"Server: Timed out reassembly: msgType=0x{m:X2}, received {state.receivedCount}/{state.fragCount}")

        // let workerLoop (client: System.Net.Sockets.UdpClient) (workerId: int) (ct: CancellationToken) =
        //     task {
        //         Logger.logTrace (fun () -> $"UDP server worker {workerId} started")
        //
        //         while not ct.IsCancellationRequested do
        //             try
        //                 let! workItem = workChannel.Reader.ReadAsync(ct).AsTask()
        //                 let (msgType, clientId, requestId, logicalPayload, remoteEp) = workItem
        //
        //                 try
        //                     Logger.logTrace (fun () -> $"Worker {workerId}: Processing msgType=0x{msgType:X2}, clientId={clientId.value}, requestId={requestId}, payloadLen={logicalPayload.Length}, remoteEp={remoteEp}")
        //
        //                     endpointMap[clientId] <- remoteEp
        //
        //                     let responseFragments = processLegacyRequest msgType clientId requestId logicalPayload
        //
        //                     for fragment in responseFragments do
        //                         client.Send(fragment, fragment.Length, remoteEp) |> ignore
        //
        //                     Logger.logTrace (fun () -> $"Worker {workerId}: Sent msgType=0x{responseFragments.[0].[0]:X2}, requestId={requestId}, fragments={responseFragments.Length}")
        //                 with
        //                 | ex when ct.IsCancellationRequested -> ()
        //                 | ex -> Logger.logError $"Worker {workerId} error for requestId {requestId}: {ex.Message}"
        //             with
        //             | :? OperationCanceledException -> ()
        //             | :? ChannelClosedException -> ()
        //             | ex when ct.IsCancellationRequested -> ()
        //             | ex -> Logger.logError $"Worker {workerId} read error: {ex.Message}"
        //
        //         Logger.logTrace (fun () -> $"UDP server worker {workerId} stopped")
        //     } :> Task

        // ===== Push protocol handlers =====

        let processPushDataPacket (clientId: VpnClientId) (payload: byte[]) =
            match registry.tryGetPushSession(clientId) with
            | Some _ ->
                match service.sendPackets (clientId, [| payload |]) with
                | Ok () -> ()
                | Error e -> Logger.logWarn $"Push: registry: {registry.GetHashCode()} failed to process packet from '{clientId.value}', error: '%A{e}'."
            | None ->
                pushStats.UnknownClientDrops.Increment()
                Logger.logTrace (fun () -> $"Push: Dropped DATA from unknown client {clientId.value}")

        // ===== Combined receive loop =====

        let receiveLoop (client: System.Net.Sockets.UdpClient) (ct: CancellationToken) =
            task {
                Logger.logInfo $"Combined UDP server receive loop started on port {serverPort}"

                while not ct.IsCancellationRequested do
                    try
                        let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                        let data = client.Receive(&remoteEp)

                        // Check if this is a push datagram by examining the magic number.
                        let isPush =
                            data.Length >= 4 &&
                            data.[0] = byte ((PushMagic >>> 24) &&& 0xFFu) &&
                            data.[1] = byte ((PushMagic >>> 16) &&& 0xFFu) &&
                            data.[2] = byte ((PushMagic >>> 8) &&& 0xFFu) &&
                            data.[3] = byte (PushMagic &&& 0xFFu)

                        if isPush then
                            // Push protocol datagram.
                            pushStats.UdpRxDatagrams.Increment()
                            pushStats.UdpRxBytes.AddInt(data.Length)

                            match tryParsePushHeader data with
                            | Ok (header, payload) ->
                                let clientId = header.clientId
                                let capturedEp = IPEndPoint(remoteEp.Address, remoteEp.Port)
                                registry.updatePushEndpoint(clientId, capturedEp)

                                match header.msgType with
                                | msgType when msgType = PushMsgTypeData ->
                                    processPushDataPacket clientId payload
                                | msgType when msgType = PushMsgTypeKeepalive ->
                                    Logger.logTrace (fun () -> $"Push: Keepalive from {clientId.value} at {capturedEp}")
                                | msgType ->
                                    Logger.logTrace (fun () -> $"Push: Unknown msgType 0x{msgType:X2} from {clientId.value}")
                            | Error () ->
                                Logger.logTrace (fun () -> $"Push: Invalid header from {remoteEp}")
                        else
                            // Legacy protocol datagram.
                            match tryParseFragmentHeader data with
                            | Ok (msgType, clientId, requestId, fragIndex, fragCount, fragmentPayload) ->
                                let key : ServerReassemblyKey = (msgType, clientId, requestId)
                                let capturedRemoteEp = IPEndPoint(remoteEp.Address, remoteEp.Port)

                                if fragCount = 1us && fragIndex = 0us then
                                    let workItem : WorkItem = (msgType, clientId, requestId, fragmentPayload, capturedRemoteEp)
                                    do! workChannel.Writer.WriteAsync(workItem, ct).AsTask()
                                else
                                    let nowTicks = Stopwatch.GetTimestamp()
                                    let state = reassemblyMap.GetOrAdd(key, fun _ -> createReassemblyState nowTicks fragCount)

                                    if state.fragCount <> fragCount then
                                        Logger.logTrace (fun () -> $"Server: Fragment count mismatch for requestId {requestId}: expected {state.fragCount}, got {fragCount}")
                                    else
                                        match tryAddFragment state fragIndex fragmentPayload with
                                        | Some logicalPayload ->
                                            reassemblyMap.TryRemove(key) |> ignore
                                            let workItem : WorkItem = (msgType, clientId, requestId, logicalPayload, capturedRemoteEp)
                                            do! workChannel.Writer.WriteAsync(workItem, ct).AsTask()
                                        | None -> ()
                            | Error () ->
                                Logger.logTrace (fun () -> $"Dropped packet with invalid header from {remoteEp}")

                        cleanupReassemblies ()

                        if pushStats.ShouldLog() then
                            Logger.logInfo (pushStats.GetSummary())
                    with
                    | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                        cleanupReassemblies ()
                        if pushStats.ShouldLog() then
                            Logger.logInfo (pushStats.GetSummary())
                    | :? SocketException as ex when ex.SocketErrorCode = SocketError.Interrupted -> ()
                    | :? SocketException as ex when ex.SocketErrorCode = SocketError.MessageSize ->
                        Logger.logWarn "MessageSize: inbound datagram too large — verify client fragmentation; dropping."
                    | :? OperationCanceledException -> ()
                    | ex when ct.IsCancellationRequested -> ()
                    | ex ->
                        Logger.logError $"UDP receive error: {ex.Message}"

                Logger.logInfo "Combined UDP server receive loop stopped"
            } :> Task

        // ===== Push send loop =====

        let pushSendLoop (client: System.Net.Sockets.UdpClient) (ct: CancellationToken) =
            task {
                Logger.logInfo "Push UDP server send loop started"

                while not ct.IsCancellationRequested do
                    try
                        let sessions = registry.getPushSessionsWithPendingPackets()

                        if sessions.IsEmpty then
                            do! Task.Delay(1, ct)
                        else
                            for session in sessions do
                                match session.currentEndpoint with
                                | Some endpoint ->
                                    match session.pendingPackets.TryDequeue() with
                                    | Some packet ->
                                        if packet.Length > PushMaxPayload then
                                            Logger.logWarn $"Push: Dropping oversized packet ({packet.Length} > {PushMaxPayload}) for {session.clientId.value}"
                                        else
                                            let seq = registry.getNextPushSeq(session.clientId)
                                            let datagram = buildPushData session.clientId seq packet

                                            try
                                                client.Send(datagram, datagram.Length, endpoint) |> ignore
                                                pushStats.UdpTxDatagrams.Increment()
                                                pushStats.UdpTxBytes.AddInt(datagram.Length)
                                            with
                                            | ex ->
                                                Logger.logWarn $"Push: Send failed to {endpoint} for {session.clientId.value}: {ex.Message}"
                                    | None -> ()
                                | None ->
                                    pushStats.NoEndpointDrops.Increment()
                    with
                    | :? OperationCanceledException -> ()
                    | ex when ct.IsCancellationRequested -> ()
                    | ex ->
                        Logger.logError $"Push UDP send error: {ex.Message}"

                Logger.logInfo "Push UDP server send loop stopped"
            } :> Task

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                Logger.logInfo $"Starting Combined UDP server on port {serverPort}"

                let cts = new CancellationTokenSource()
                cancellationTokenSource <- Some cts

                let client = new System.Net.Sockets.UdpClient(serverPort)
                client.Client.ReceiveTimeout <- ServerReceiveTimeoutMs
                udpClient <- Some client

                // // Start worker tasks for legacy protocol.
                // for i = 0 to workerCount - 1 do
                //     Task.Run(fun () -> workerLoop client i cts.Token) |> ignore

                // Start receive loop (handles both protocols).
                Task.Run(fun () -> receiveLoop client cts.Token) |> ignore

                // Start push send loop.
                Task.Run(fun () -> pushSendLoop client cts.Token) |> ignore

                Task.CompletedTask

            member _.StopAsync(cancellationToken: CancellationToken) =
                Logger.logInfo "Stopping Combined UDP server"

                match cancellationTokenSource with
                | Some cts ->
                    cts.Cancel()
                    cts.Dispose()
                    cancellationTokenSource <- None
                | None -> ()

                match udpClient with
                | Some client ->
                    client.Close()
                    client.Dispose()
                    udpClient <- None
                | None -> ()

                Task.CompletedTask


    let getCombinedUdpHostedService (data: VpnServerData) (service: IVpnPushService) (registry: ClientRegistry) : IHostedService =
        VpnCombinedUdpHostedService(data, service, registry) :> IHostedService


    // let getCombinedUdpProgram (data: VpnServerData) (getService: unit -> VpnService) (argv: string[]) =
    //     fun () ->
    //         try
    //             let service = getService()
    //             let registry = service.clientRegistry
    //
    //             let host =
    //                 Host.CreateDefaultBuilder(argv)
    //                     .ConfigureServices(fun services ->
    //                         // Register the IVpnService instance.
    //                         services.AddSingleton<IVpnService>(service :> IVpnService) |> ignore
    //
    //                         // Register IVpnService as IHostedService (it implements both).
    //                         services.AddSingleton<IHostedService>(service :> IHostedService) |> ignore
    //
    //                         // Register the Combined UDP-hosted service.
    //                         let combinedUdpHostedService = getCombinedUdpHostedService data service registry
    //                         services.AddSingleton<IHostedService>(combinedUdpHostedService) |> ignore
    //                     )
    //                     .Build()
    //
    //             Logger.logInfo $"Combined UDP VPN Server starting with subnet: {data.serverAccessInfo.vpnSubnet.value}"
    //             host.Run()
    //             CompletedSuccessfully
    //         with
    //         | ex ->
    //             Logger.logCrit $"Combined UDP VPN Server failed: {ex.Message}"
    //             CriticalError
