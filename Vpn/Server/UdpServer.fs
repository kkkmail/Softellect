namespace Softellect.Vpn.Server

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Softellect.Sys.Logging
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Core.UdpProtocol
open Softellect.Vpn.Server.ClientRegistry
open Softellect.Vpn.Server.Service

module UdpServer =

    /// Reassembly key for server: (msgType, clientId, requestId).
    type private ServerReassemblyKey = byte * VpnClientId * uint32


    type VpnCombinedUdpHostedService(data: VpnServerData, service: IVpnPushService, registry: ClientRegistry) =
        do Logger.logInfo $"Using registry: {registry.GetHashCode()}."

        let serverPort = data.serverAccessInfo.serviceAccessInfo.getServicePort().value
        let reassemblyMap = ConcurrentDictionary<ServerReassemblyKey, ReassemblyState>()
        let pushStats = ServerPushStats()
        let mutable udpClient : UdpClient option = None
        let mutable cancellationTokenSource : CancellationTokenSource option = None
        let reassemblyTimeoutTicks = int64 ServerReassemblyTimeoutMs * Stopwatch.Frequency / 1000L

        let cleanupReassemblies () =
            let nowTicks = Stopwatch.GetTimestamp()
            for kvp in reassemblyMap do
                let key = kvp.Key
                let state = kvp.Value
                if nowTicks - state.createdAtTicks > reassemblyTimeoutTicks then
                    reassemblyMap.TryRemove(key) |> ignore
                    let m, _, _ = key
                    Logger.logTrace (fun () -> $"Server: Timed out reassembly: msgType=0x{m:X2}, received {state.receivedCount}/{state.fragCount}")

        let processPushDataPacket (clientId: VpnClientId) (payload: byte[]) =
            match registry.tryGetPushSession(clientId) with
            | Some _ ->
                match service.sendPackets (clientId, [| payload |]) with
                | Ok () ->
                    Logger.logTrace (fun () -> $"Push: registry: {registry.GetHashCode()} sent {payload.Length} bytes to '{clientId.value}'.")
                    ()
                | Error e -> Logger.logWarn $"Push: registry: {registry.GetHashCode()} failed to process packet from '{clientId.value}', error: '%A{e}'."
            | None ->
                pushStats.unknownClientDrops.increment()
                Logger.logTrace (fun () -> $"Push: Dropped DATA from unknown client {clientId.value}")

        let receiveLoop (client: UdpClient) (ct: CancellationToken) =
            task {
                Logger.logInfo $"Combined UDP server receive loop started on port {serverPort}"

                while not ct.IsCancellationRequested do
                    try
                        let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                        let data = client.Receive(&remoteEp)

                        pushStats.udpRxDatagrams.increment()
                        pushStats.udpRxBytes.addInt(data.Length)

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
                        | Error () -> Logger.logTrace (fun () -> $"Push: Invalid header from {remoteEp}")
                        cleanupReassemblies ()

                        if pushStats.shouldLog() then
                            Logger.logInfo (pushStats.getSummary())
                    with
                    | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                        cleanupReassemblies ()
                        if pushStats.shouldLog() then
                            Logger.logInfo (pushStats.getSummary())
                    | :? SocketException as ex when ex.SocketErrorCode = SocketError.Interrupted -> ()
                    | :? SocketException as ex when ex.SocketErrorCode = SocketError.MessageSize ->
                        Logger.logWarn "MessageSize: inbound datagram too large — verify client fragmentation; dropping."
                    | :? OperationCanceledException -> ()
                    | _ when ct.IsCancellationRequested -> ()
                    | ex ->
                        Logger.logError $"UDP receive error: {ex.Message}"

                Logger.logInfo "Combined UDP server receive loop stopped"
            } :> Task

        let pushSendLoop (client: UdpClient) (ct: CancellationToken) =
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
                                    let packets = session.pendingPackets.dequeueMany(MaxSendPacketsPerCall)
                                    if packets.Length = 0 then ()
                                    else
                                        Logger.logTrace (fun () -> $"Push: dequeued {packets.Length} packets.")
                                        for packet in packets do
                                            if packet.Length > PushMaxPayload then
                                                Logger.logWarn $"Push: Dropping oversized packet ({packet.Length} > {PushMaxPayload}) for {session.clientId.value}"
                                            else
                                                let seq = registry.getNextPushSeq(session.clientId)
                                                let datagram = buildPushData session.clientId seq packet

                                                try
                                                    client.Send(datagram, datagram.Length, endpoint) |> ignore
                                                    pushStats.udpTxDatagrams.increment()
                                                    pushStats.udpTxBytes.addInt(datagram.Length)
                                                with
                                                | ex -> Logger.logWarn $"Push: Send failed to {endpoint} for {session.clientId.value}: {ex.Message}"
                                | None -> pushStats.noEndpointDrops.increment()
                    with
                    | :? OperationCanceledException -> ()
                    | _ when ct.IsCancellationRequested -> ()
                    | ex -> Logger.logError $"Push UDP send error: {ex.Message}"

                Logger.logInfo "Push UDP server send loop stopped"
            } :> Task

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                Logger.logInfo $"Starting Combined UDP server on port {serverPort}"

                let cts = new CancellationTokenSource()
                cancellationTokenSource <- Some cts

                let client = new UdpClient(serverPort)
                client.Client.ReceiveTimeout <- ServerReceiveTimeoutMs
                udpClient <- Some client

                Task.Run(fun () -> receiveLoop client cts.Token) |> ignore
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


    let getCombinedUdpHostedService (data: VpnServerData) (service: IVpnPushService) (registry: ClientRegistry) =
        VpnCombinedUdpHostedService(data, service, registry)
