namespace Softellect.Vpn.Client

open System
open System.Net
open System.Net.Sockets
open Softellect.Sys.Core
open Softellect.Sys.Logging
open Softellect.Wcf.Common
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Core.PacketDebug

module UdpClient =

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
    let private ReceiveTimeoutMs = 2000


    let private buildDatagram (msgType: byte) (clientId: VpnClientId) (payload: byte[]) : byte[] =
        let guidBytes = clientId.value.ToByteArray()
        let result = Array.zeroCreate (1 + 16 + payload.Length)
        result.[0] <- msgType
        Array.Copy(guidBytes, 0, result, 1, 16)
        Array.Copy(payload, 0, result, 17, payload.Length)
        result


    let private parseResponse (expectedMsgType: byte) (expectedClientId: VpnClientId) (data: byte[]) : Result<byte[], VpnError> =
        if data.Length < HeaderSize then
            Error (ConnectionErr (ServerUnreachableErr "Response too short"))
        else
            let msgType = data.[0]
            let guidBytes = data.[1..16]
            let responseClientId = Guid(guidBytes) |> VpnClientId
            let payload = if data.Length > HeaderSize then data.[HeaderSize..] else [||]

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
                Error (ConfigErr $"Client ID mismatch in response")
            else
                Ok payload


    type VpnUdpClient(data: VpnClientAccessInfo) =
        let clientId = data.vpnClientId
        let serverIp = data.serverAccessInfo.getIpAddress()
        let serverPort = data.serverAccessInfo.getServicePort().value
        let serverEndpoint = IPEndPoint(serverIp.ipAddress, serverPort)

        let udpClient = new System.Net.Sockets.UdpClient()

        do
            udpClient.Client.ReceiveTimeout <- ReceiveTimeoutMs
            Logger.logInfo $"VpnUdpClient created - Server: {serverIp}:{serverPort}, ClientId: {clientId.value}"

        let sendAndReceive (datagram: byte[]) : Result<byte[], VpnError> =
            try
                udpClient.Send(datagram, datagram.Length, serverEndpoint) |> ignore
                let mutable remoteEp = serverEndpoint
                let response = udpClient.Receive(&remoteEp)
                Ok response
            with
            | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                Error (ConnectionErr ConnectionTimeoutErr)
            | ex ->
                Error (ConnectionErr (ServerUnreachableErr ex.Message))

        interface IVpnClient with
            member _.authenticate request =
                Logger.logTrace (fun () -> $"authenticate: Sending auth request for client {request.clientId.value}")

                match trySerialize wcfSerializationFormat request with
                | Ok payload ->
                    let datagram = buildDatagram MsgTypeAuthenticate request.clientId payload

                    match sendAndReceive datagram with
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
                Logger.logTracePackets (packets, (fun () -> $"Sending for client {clientId.value}: "))

                match trySerialize wcfSerializationFormat packets with
                | Ok payload ->
                    let datagram = buildDatagram MsgTypeSendPackets clientId payload

                    match sendAndReceive datagram with
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

                // Payload is empty for receivePackets - clientId is in header
                let datagram = buildDatagram MsgTypeReceivePackets reqClientId [||]

                let result =
                    match sendAndReceive datagram with
                    | Ok responseData ->
                        match parseResponse MsgTypeReceivePacketsResponse reqClientId responseData with
                        | Ok responsePayload ->
                            match tryDeserialize<VpnPacketsResult> wcfSerializationFormat responsePayload with
                            | Ok result -> result
                            | Error e -> Error (ConfigErr $"Deserialization error: {e}")
                        | Error e -> Error e
                    | Error e -> Error e

                match result with
                | Ok (Some r) -> Logger.logTracePackets (r, (fun () -> $"Received for client {reqClientId.value}: "))
                | Ok None -> Logger.logTrace (fun () -> "Empty response.")
                | Error e -> Logger.logWarn $"ERROR: '{e}'."

                result

        interface IDisposable with
            member _.Dispose() =
                udpClient.Close()
                udpClient.Dispose()


    let createVpnUdpClient (clientAccessInfo: VpnClientAccessInfo) : IVpnClient =
        VpnUdpClient(clientAccessInfo) :> IVpnClient
