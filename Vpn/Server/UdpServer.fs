namespace Softellect.Vpn.Server

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Net
open System.Net.Sockets
open System.Threading
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

module UdpServer =

    /// Reassembly key for server: (msgType, clientId, requestId).
    type private ServerReassemblyKey = byte * VpnClientId * uint32

    /// Poll sleep interval for long-poll (10ms).
    [<Literal>]
    let private PollSleepMs = 10

    type VpnUdpHostedService(data: VpnServerData, service: IVpnService) =
        let serverPort = data.serverAccessInfo.serviceAccessInfo.getServicePort().value
        let endpointMap = ConcurrentDictionary<VpnClientId, IPEndPoint>()
        let reassemblyMap = ConcurrentDictionary<ServerReassemblyKey, ReassemblyState>()
        let mutable udpClient : System.Net.Sockets.UdpClient option = None
        let mutable cancellationTokenSource : CancellationTokenSource option = None

        let reassemblyTimeoutTicks = int64 ServerReassemblyTimeoutMs * Stopwatch.Frequency / 1000L

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

            // Long-poll with frequent checks instead of sleeping the full duration.
            let sw = Stopwatch.StartNew()
            let mutable result = service.receivePackets clientId
            let mutable shouldContinue = true

            while shouldContinue do
                match result with
                | Ok (Some _) ->
                    // Got packets - stop polling.
                    shouldContinue <- false
                | Error _ ->
                    // Error - stop polling.
                    shouldContinue <- false
                | Ok None ->
                    // No packets yet - check if we should keep polling.
                    if sw.ElapsedMilliseconds >= int64 receiveRequest.maxWaitMs then
                        shouldContinue <- false
                    else
                        Thread.Sleep(PollSleepMs)
                        result <- service.receivePackets clientId

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

        let receiveLoop (client: System.Net.Sockets.UdpClient) (ct: CancellationToken) =
            Logger.logInfo $"UDP server receive loop started on port {serverPort}"

            while not ct.IsCancellationRequested do
                try
                    let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                    let data = client.Receive(&remoteEp)

                    match tryParseFragmentHeader data with
                    | Ok (msgType, clientId, requestId, fragIndex, fragCount, fragmentPayload) ->
                        let key : ServerReassemblyKey = (msgType, clientId, requestId)

                        let processAndRespond (logicalPayload: byte[]) =
                            Logger.logTrace (fun () -> $"Received: msgType=0x{msgType:X2}, clientId={clientId.value}, requestId={requestId}, payloadLen={logicalPayload.Length}, remoteEp={remoteEp}")

                            // Update endpoint mapping.
                            endpointMap[clientId] <- remoteEp

                            // Process request and send all response fragments.
                            let responseFragments = processRequest msgType clientId requestId logicalPayload
                            for fragment in responseFragments do
                                client.Send(fragment, fragment.Length, remoteEp) |> ignore

                            Logger.logTrace (fun () -> $"Sent: msgType=0x{responseFragments.[0].[0]:X2}, requestId={requestId}, fragments={responseFragments.Length}")

                        if fragCount = 1us && fragIndex = 0us then
                            // Single fragment - process immediately.
                            processAndRespond fragmentPayload
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
                                    // Reassembly complete - remove from map and process.
                                    reassemblyMap.TryRemove(key) |> ignore
                                    processAndRespond logicalPayload
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
                | ex when ct.IsCancellationRequested ->
                    // Shutting down.
                    ()
                | ex ->
                    Logger.logError $"UDP receive error: {ex.Message}"

            Logger.logInfo "UDP server receive loop stopped"

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                Logger.logInfo $"Starting UDP server on port {serverPort}"

                let cts = new CancellationTokenSource()
                cancellationTokenSource <- Some cts

                let client = new System.Net.Sockets.UdpClient(serverPort)
                client.Client.ReceiveTimeout <- ServerReceiveTimeoutMs
                udpClient <- Some client

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

                            // Register the UDP hosted service.
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
