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

    /// Reassembly key for client: (msgType, clientId, requestId).
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

        /// Send a request and wait for the response.
        let sendRequest (requestMsgType: byte) (reqClientId: VpnClientId) (payload: byte[]) : Result<ResponseData, VpnError> =
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
                // Build and send all fragments.
                let fragments = buildFragments requestMsgType reqClientId requestId payload
                for fragment in fragments do
                    udpClient.Send(fragment, fragment.Length) |> ignore

                // Wait for completion with a hard stop timeout.
                let hardTimeoutMs = RequestTimeoutMs + CleanupIntervalMs
                if tcs.Task.Wait(hardTimeoutMs) then
                    Ok tcs.Task.Result
                else
                    // Hard timeout - remove pending and return error.
                    pendingRequests.TryRemove(requestId) |> ignore
                    Error (ConnectionErr ConnectionTimeoutErr)
            with
            | :? AggregateException as ae when (ae.InnerException :? TimeoutException) ->
                Error (ConnectionErr ConnectionTimeoutErr)
            | :? TimeoutException ->
                Error (ConnectionErr ConnectionTimeoutErr)
            | :? SocketException as ex ->
                pendingRequests.TryRemove(requestId) |> ignore
                Error (ConnectionErr (ServerUnreachableErr ex.Message))
            | ex ->
                pendingRequests.TryRemove(requestId) |> ignore
                Error (ConnectionErr (ServerUnreachableErr ex.Message))

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
        VpnUdpClient(clientAccessInfo) :> IVpnClient
