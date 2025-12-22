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
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Core.UdpProtocol
open Softellect.Vpn.Server.ClientRegistry
open Softellect.Vpn.Server.Service

module UdpServer =

    /// Reassembly key for server: (cmd, clientId, requestId).
    type private ServerReassemblyKey = byte * VpnClientId * uint32


    type VpnCombinedUdpHostedService(data: VpnServerData, service: IVpnPushService, registry: ClientRegistry) =
        do Logger.logInfo $"Using registry: {registry.GetHashCode()}."

        let serverPort = data.serverAccessInfo.serviceAccessInfo.getServicePort().value
        let serverPrivateKey = data.serverPrivateKey
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
                    Logger.logTrace (fun () -> $"Server: Timed out reassembly: cmd=0x{m:X2}, received {state.receivedCount}/{state.fragCount}")

        /// Kick a client session with error logging (once per client).
        let kickSession (clientId: VpnClientId) (reason: string) =
            Logger.logError $"Kicking client {clientId.value}: {reason}"
            registry.removePushSession(clientId)

        let processPushDataPacket (clientId: VpnClientId) (packetData: byte[]) =
            match registry.tryGetPushSession(clientId) with
            | Some _ ->
                match service.sendPackets (clientId, [| packetData |]) with
                | Ok () ->
                    Logger.logTrace (fun () -> $"Push: registry: {registry.GetHashCode()} sent {packetData.Length} bytes to '{clientId.value}'.")
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
                        let rawData = client.Receive(&remoteEp)

                        pushStats.udpRxDatagrams.increment()
                        pushStats.udpRxBytes.addInt(rawData.Length)

                        match tryParsePushDatagram rawData with
                        | Ok (clientId, payloadBytes) ->
                            let capturedEp = IPEndPoint(remoteEp.Address, remoteEp.Port)

                            match registry.tryGetPushSession(clientId) with
                            | Some session ->
                                registry.updatePushEndpoint(clientId, capturedEp)

                                // Decrypt if session expects encryption
                                let plaintextResult =
                                    if session.useEncryption then
                                        match tryDecryptAndVerify session.encryptionType payloadBytes serverPrivateKey session.publicKey with
                                        | Ok decrypted -> Ok decrypted
                                        | Error e ->
                                            kickSession clientId $"Decryption/verification failed: %A{e}"
                                            Error ()
                                    else
                                        Ok payloadBytes

                                match plaintextResult with
                                | Ok plaintextPayload ->
                                    match tryParsePayload plaintextPayload with
                                    | Ok (cmd, cmdData) ->
                                        match cmd with
                                        | c when c = PushCmdData ->
                                            processPushDataPacket clientId cmdData
                                        | c when c = PushCmdKeepalive ->
                                            Logger.logTrace (fun () -> $"Push: Keepalive from {clientId.value} at {capturedEp}")
                                        | c ->
                                            Logger.logTrace (fun () -> $"Push: Unknown cmd 0x{c:X2} from {clientId.value}")
                                    | Error () ->
                                        if session.useEncryption then
                                            kickSession clientId "Failed to parse decrypted payload"
                                        else
                                            Logger.logTrace (fun () -> $"Push: Invalid payload from {clientId.value}")
                                | Error () -> ()  // Already kicked
                            | None ->
                                pushStats.unknownClientDrops.increment()
                                Logger.logTrace (fun () -> $"Push: Dropped packet from unknown client {clientId.value}")
                        | Error () -> Logger.logTrace (fun () -> $"Push: Invalid datagram from {remoteEp}")
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
                                        let mutable sessionKicked = false

                                        let sends =
                                            packets
                                            |> Seq.choose (fun packet ->
                                                if sessionKicked then None
                                                else
                                                    // Build plaintext payload with command
                                                    let plaintextPayload = buildPayload PushCmdData packet

                                                    // Encrypt if session expects encryption
                                                    let finalPayloadResult =
                                                        if session.useEncryption then
                                                            match tryEncryptAndSign session.encryptionType plaintextPayload serverPrivateKey session.publicKey with
                                                            | Ok encrypted -> Ok encrypted
                                                            | Error e ->
                                                                kickSession session.clientId $"Encryption failed: %A{e}"
                                                                sessionKicked <- true
                                                                Error ()
                                                        else
                                                            Ok plaintextPayload

                                                    match finalPayloadResult with
                                                    | Ok finalPayload ->
                                                        if finalPayload.Length > PushMaxPayload then
                                                            Logger.logWarn $"Push: Dropping oversized packet ({finalPayload.Length} > {PushMaxPayload}) for {session.clientId.value}"
                                                            None
                                                        else
                                                            let datagram = buildPushDatagram session.clientId finalPayload
                                                            Some (client.SendAsync(datagram, datagram.Length, endpoint))
                                                    | Error () -> None)

                                        let! _ = Task.WhenAll(sends)
                                        pushStats.udpTxDatagrams.addInt(packets.Length)

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
                client.Client.SendBufferSize <- SendBufferSize
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
