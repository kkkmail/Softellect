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

module UdpServer =

    // Message types for UDP protocol
    [<Literal>]
    let private MsgTypeAuthenticate = 0x01uy

    [<Literal>]
    let private MsgTypeSendPackets = 0x02uy

    [<Literal>]
    let private MsgTypeReceivePackets = 0x03uy

    [<Literal>]
    let private MsgTypeAuthenticateResponse = 0x81uy

    [<Literal>]
    let private MsgTypeSendPacketsResponse = 0x82uy

    [<Literal>]
    let private MsgTypeReceivePacketsResponse = 0x83uy

    [<Literal>]
    let private MsgTypeErrorResponse = 0xFFuy

    [<Literal>]
    let private HeaderSize = 17 // 1 byte msgType + 16 bytes GUID

    [<Literal>]
    let private ReceiveTimeoutMs = 250


    let private buildResponse (msgType: byte) (clientId: VpnClientId) (payload: byte[]) : byte[] =
        let guidBytes = clientId.value.ToByteArray()
        let result = Array.zeroCreate (1 + 16 + payload.Length)
        result[0] <- msgType
        Array.Copy(guidBytes, 0, result, 1, 16)
        Array.Copy(payload, 0, result, 17, payload.Length)
        result


    let private buildErrorResponse (clientId: VpnClientId) (errorMsg: string) : byte[] =
        let msgBytes = System.Text.Encoding.UTF8.GetBytes(errorMsg)
        let truncated = if msgBytes.Length > 1024 then msgBytes[..1023] else msgBytes
        buildResponse MsgTypeErrorResponse clientId truncated


    let private parseHeader (data: byte[]) : Result<byte * VpnClientId * byte[], unit> =
        if data.Length < HeaderSize then
            Error ()
        else
            let msgType = data[0]
            let guidBytes = data[1..16]
            let clientId = Guid(guidBytes) |> VpnClientId
            let payload = if data.Length > HeaderSize then data[HeaderSize..] else [||]
            Ok (msgType, clientId, payload)


    type VpnUdpHostedService(data: VpnServerData, service: IVpnService) =
        let serverPort = data.serverAccessInfo.serviceAccessInfo.getServicePort().value
        let endpointMap = ConcurrentDictionary<VpnClientId, IPEndPoint>()
        let mutable udpClient : System.Net.Sockets.UdpClient option = None
        let mutable cancellationTokenSource : CancellationTokenSource option = None

        let processAuthenticate (clientId: VpnClientId) (payload: byte[]) : byte[] =
            match tryDeserialize<VpnAuthRequest> wcfSerializationFormat payload with
            | Ok request ->
                let result = service.authenticate request
                match trySerialize wcfSerializationFormat result with
                | Ok responsePayload -> buildResponse MsgTypeAuthenticateResponse clientId responsePayload
                | Error _ -> buildErrorResponse clientId "Serialization error"
            | Error _ ->
                buildErrorResponse clientId "Invalid authenticate payload"

        let processSendPackets (clientId: VpnClientId) (payload: byte[]) : byte[] =
            match tryDeserialize<byte[][]> wcfSerializationFormat payload with
            | Ok packets ->
                let result = service.sendPackets (clientId, packets)
                match trySerialize wcfSerializationFormat result with
                | Ok responsePayload -> buildResponse MsgTypeSendPacketsResponse clientId responsePayload
                | Error _ -> buildErrorResponse clientId "Serialization error"
            | Error _ ->
                buildErrorResponse clientId "Invalid sendPackets payload"

        let processReceivePackets (clientId: VpnClientId) : byte[] =
            let result = service.receivePackets clientId
            match trySerialize wcfSerializationFormat result with
            | Ok responsePayload -> buildResponse MsgTypeReceivePacketsResponse clientId responsePayload
            | Error _ -> buildErrorResponse clientId "Serialization error"

        let processRequest (msgType: byte) (clientId: VpnClientId) (payload: byte[]) : byte[] =
            match msgType with
            | 0x01uy -> processAuthenticate clientId payload
            | 0x02uy -> processSendPackets clientId payload
            | 0x03uy -> processReceivePackets clientId
            | _ -> buildErrorResponse clientId $"Unknown message type: 0x{msgType:X2}"

        let receiveLoop (client: System.Net.Sockets.UdpClient) (ct: CancellationToken) =
            Logger.logInfo $"UDP server receive loop started on port {serverPort}"

            while not ct.IsCancellationRequested do
                try
                    let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                    let data = client.Receive(&remoteEp)

                    match parseHeader data with
                    | Ok (msgType, clientId, payload) ->
                        // Update endpoint mapping
                        endpointMap[clientId] <- remoteEp

                        // Process request and send response
                        let response = processRequest msgType clientId payload
                        client.Send(response, response.Length, remoteEp) |> ignore
                    | Error () ->
                        // Invalid header - drop silently
                        ()
                with
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                    // Timeout is expected - allows checking cancellation
                    ()
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.Interrupted ->
                    // Socket was closed during shutdown
                    ()
                | ex when ct.IsCancellationRequested ->
                    // Shutting down
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
                client.Client.ReceiveTimeout <- ReceiveTimeoutMs
                udpClient <- Some client

                // Start receive loop on background thread
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
                            // Register the IVpnService instance
                            services.AddSingleton<IVpnService>(service) |> ignore

                            // Register IVpnService as IHostedService (it implements both)
                            services.AddSingleton<IHostedService>(service :> IHostedService) |> ignore

                            // Register the UDP hosted service
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
