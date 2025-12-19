namespace Softellect.Vpn.Server

open System
open System.Collections.Concurrent
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

    type VpnUdpHostedService(data: VpnServerData, service: IVpnService) =
        let serverPort = data.serverAccessInfo.serviceAccessInfo.getServicePort().value
        let endpointMap = ConcurrentDictionary<VpnClientId, IPEndPoint>()
        let mutable udpClient : System.Net.Sockets.UdpClient option = None
        let mutable cancellationTokenSource : CancellationTokenSource option = None

        let processAuthenticate (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[] =
            Logger.logTrace (fun () -> $"processAuthenticate: clientId={clientId.value}, requestId={requestId}, payloadLen={payload.Length}")
            match tryDeserialize<VpnAuthRequest> wcfSerializationFormat payload with
            | Ok request ->
                let result = service.authenticate request
                match trySerialize wcfSerializationFormat result with
                | Ok responsePayload -> buildDatagram MsgTypeAuthenticateResponse clientId requestId responsePayload
                | Error _ -> buildErrorResponse clientId requestId "Serialization error"
            | Error _ ->
                buildErrorResponse clientId requestId "Invalid authenticate payload"

        let processSendPackets (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[] =
            Logger.logTrace (fun () -> $"processSendPackets: clientId={clientId.value}, requestId={requestId}, payloadLen={payload.Length}")
            match tryDeserialize<byte[][]> wcfSerializationFormat payload with
            | Ok packets ->
                let result = service.sendPackets (clientId, packets)
                match trySerialize wcfSerializationFormat result with
                | Ok responsePayload -> buildDatagram MsgTypeSendPacketsResponse clientId requestId responsePayload
                | Error _ -> buildErrorResponse clientId requestId "Serialization error"
            | Error _ ->
                buildErrorResponse clientId requestId "Invalid sendPackets payload"

        let processReceivePackets (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[] =
            Logger.logTrace (fun () -> $"processReceivePackets: clientId={clientId.value}, requestId={requestId}, payloadLen={payload.Length}")

            // Deserialize long-poll request parameters.
            let receiveRequest =
                match tryDeserialize<ReceivePacketsRequest> wcfSerializationFormat payload with
                | Ok req -> req
                | Error _ -> { maxWaitMs = DefaultMaxWaitMs; maxPackets = DefaultMaxPackets }

            // First attempt to receive packets.
            let mutable result = service.receivePackets clientId

            // If no packets, wait and try once more (long-poll).
            match result with
            | Ok None when receiveRequest.maxWaitMs > 0 ->
                Thread.Sleep(receiveRequest.maxWaitMs)
                result <- service.receivePackets clientId
            | _ -> ()

            match trySerialize wcfSerializationFormat result with
            | Ok responsePayload -> buildDatagram MsgTypeReceivePacketsResponse clientId requestId responsePayload
            | Error _ -> buildErrorResponse clientId requestId "Serialization error"

        let processRequest (msgType: byte) (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[] =
            match msgType with
            | 0x01uy -> processAuthenticate clientId requestId payload
            | 0x02uy -> processSendPackets clientId requestId payload
            | 0x03uy -> processReceivePackets clientId requestId payload
            | _ -> buildErrorResponse clientId requestId $"Unknown message type: 0x{msgType:X2}"

        let receiveLoop (client: System.Net.Sockets.UdpClient) (ct: CancellationToken) =
            Logger.logInfo $"UDP server receive loop started on port {serverPort}"

            while not ct.IsCancellationRequested do
                try
                    let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                    let data = client.Receive(&remoteEp)

                    match tryParseHeader data with
                    | Ok (msgType, clientId, requestId, payload) ->
                        Logger.logTrace (fun () -> $"Received: msgType=0x{msgType:X2}, clientId={clientId.value}, requestId={requestId}, payloadLen={payload.Length}, remoteEp={remoteEp}")

                        // Update endpoint mapping.
                        endpointMap[clientId] <- remoteEp

                        // Process request and send response.
                        let response = processRequest msgType clientId requestId payload
                        client.Send(response, response.Length, remoteEp) |> ignore

                        Logger.logTrace (fun () -> $"Sent: msgType=0x{response.[0]:X2}, requestId={requestId}, len={response.Length}")
                    | Error () ->
                        // Invalid header - drop silently.
                        Logger.logTrace (fun () -> $"Dropped packet with invalid header from {remoteEp}")
                with
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                    // Timeout is expected - allows checking cancellation.
                    ()
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.Interrupted ->
                    // Socket was closed during shutdown.
                    ()
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
