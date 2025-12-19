namespace Softellect.Vpn.Core

open System
open Softellect.Vpn.Core.Primitives

module UdpProtocol =

    // === Message type constants ===

    [<Literal>]
    let MsgTypeAuthenticate = 0x01uy

    [<Literal>]
    let MsgTypeSendPackets = 0x02uy

    [<Literal>]
    let MsgTypeReceivePackets = 0x03uy

    [<Literal>]
    let MsgTypeAuthenticateResponse = 0x81uy

    [<Literal>]
    let MsgTypeSendPacketsResponse = 0x82uy

    [<Literal>]
    let MsgTypeReceivePacketsResponse = 0x83uy

    [<Literal>]
    let MsgTypeErrorResponse = 0xFFuy

    // === Header layout: msgType (1) + clientId (16) + requestId (4) = 21 bytes ===

    [<Literal>]
    let HeaderSize = 21

    // === Client-side constants ===

    [<Literal>]
    let RequestTimeoutMs = 2000

    [<Literal>]
    let CleanupIntervalMs = 250

    [<Literal>]
    let DefaultMaxWaitMs = 1000

    [<Literal>]
    let DefaultMaxPackets = 256

    // === Server-side constants ===

    [<Literal>]
    let ServerReceiveTimeoutMs = 250


    /// Build a datagram with header: msgType (1 byte) + clientId (16 bytes) + requestId (4 bytes) + payload
    let buildDatagram (msgType: byte) (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[] =
        let guidBytes = clientId.value.ToByteArray()
        let requestIdBytes = BitConverter.GetBytes(requestId) // little-endian
        let result = Array.zeroCreate (HeaderSize + payload.Length)
        result.[0] <- msgType
        Array.Copy(guidBytes, 0, result, 1, 16)
        Array.Copy(requestIdBytes, 0, result, 17, 4)
        Array.Copy(payload, 0, result, HeaderSize, payload.Length)
        result


    /// Parse a datagram header.
    /// Returns Ok (msgType, clientId, requestId, payload) or Error () if header is too short.
    let tryParseHeader (data: byte[]) : Result<byte * VpnClientId * uint32 * byte[], unit> =
        if data.Length < HeaderSize then
            Error ()
        else
            let msgType = data.[0]
            let guidBytes = data.[1..16]
            let clientId = Guid(guidBytes) |> VpnClientId
            let requestId = BitConverter.ToUInt32(data, 17)
            let payload = if data.Length > HeaderSize then data.[HeaderSize..] else [||]
            Ok (msgType, clientId, requestId, payload)


    /// Build an error response datagram.
    let buildErrorResponse (clientId: VpnClientId) (requestId: uint32) (errorMsg: string) : byte[] =
        let msgBytes = System.Text.Encoding.UTF8.GetBytes(errorMsg)
        let truncated = if msgBytes.Length > 1024 then msgBytes.[..1023] else msgBytes
        buildDatagram MsgTypeErrorResponse clientId requestId truncated


    /// Map request msgType to expected response msgType.
    let expectedResponseType (requestMsgType: byte) : byte =
        match requestMsgType with
        | 0x01uy -> MsgTypeAuthenticateResponse
        | 0x02uy -> MsgTypeSendPacketsResponse
        | 0x03uy -> MsgTypeReceivePacketsResponse
        | _ -> MsgTypeErrorResponse


    /// Record for receivePackets long-poll request payload.
    type ReceivePacketsRequest =
        {
            maxWaitMs : int
            maxPackets : int
        }
