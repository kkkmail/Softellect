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

    [<Literal>]
    let ServerReassemblyTimeoutMs = 2000

    // === Fragmentation constants ===

    /// Maximum UDP datagram size to avoid IP fragmentation.
    [<Literal>]
    let MaxUdpDatagramSize = 1200

    /// Fragment header size: fragIndex (2 bytes) + fragCount (2 bytes).
    [<Literal>]
    let FragHeaderSize = 4

    /// Maximum payload bytes per fragment.
    [<Literal>]
    let MaxPayloadPerFragment = MaxUdpDatagramSize - HeaderSize - FragHeaderSize  // 1175


    /// Build a datagram with header: msgType (1 byte) + clientId (16 bytes) + requestId (4 bytes) + payload
    /// Note: This is the old format without fragmentation header. Use buildFragments for new code.
    let buildDatagram (msgType: byte) (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[] =
        let guidBytes = clientId.value.ToByteArray()
        let requestIdBytes = BitConverter.GetBytes(requestId) // little-endian
        let result = Array.zeroCreate (HeaderSize + payload.Length)
        result.[0] <- msgType
        Array.Copy(guidBytes, 0, result, 1, 16)
        Array.Copy(requestIdBytes, 0, result, 17, 4)
        Array.Copy(payload, 0, result, HeaderSize, payload.Length)
        result


    /// Build a single fragment datagram with full header (base + frag).
    /// Wire layout: msgType (1) + clientId (16) + requestId (4) + fragIndex (2) + fragCount (2) + fragmentPayload
    let private buildFragmentDatagram
        (msgType: byte)
        (clientId: VpnClientId)
        (requestId: uint32)
        (fragIndex: uint16)
        (fragCount: uint16)
        (fragmentPayload: byte[])
        : byte[] =
        let fullHeaderSize = HeaderSize + FragHeaderSize
        let result = Array.zeroCreate (fullHeaderSize + fragmentPayload.Length)
        let guidBytes = clientId.value.ToByteArray()
        let requestIdBytes = BitConverter.GetBytes(requestId)
        let fragIndexBytes = BitConverter.GetBytes(fragIndex)
        let fragCountBytes = BitConverter.GetBytes(fragCount)
        result.[0] <- msgType
        Array.Copy(guidBytes, 0, result, 1, 16)
        Array.Copy(requestIdBytes, 0, result, 17, 4)
        Array.Copy(fragIndexBytes, 0, result, 21, 2)
        Array.Copy(fragCountBytes, 0, result, 23, 2)
        Array.Copy(fragmentPayload, 0, result, fullHeaderSize, fragmentPayload.Length)
        result


    /// Build fragments from a logical payload.
    /// Returns an array of datagrams, each containing base header + frag header + fragment payload.
    /// For payloads that fit in a single fragment, returns a single-element array with fragIndex=0, fragCount=1.
    let buildFragments (msgType: byte) (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[][] =
        if payload.Length <= MaxPayloadPerFragment then
            // Single fragment case.
            [| buildFragmentDatagram msgType clientId requestId 0us 1us payload |]
        else
            // Calculate number of fragments needed.
            let fragCount = (payload.Length + MaxPayloadPerFragment - 1) / MaxPayloadPerFragment
            let fragments = Array.zeroCreate fragCount
            for i = 0 to fragCount - 1 do
                let offset = i * MaxPayloadPerFragment
                let length = min MaxPayloadPerFragment (payload.Length - offset)
                let fragmentPayload = Array.sub payload offset length
                fragments.[i] <- buildFragmentDatagram msgType clientId requestId (uint16 i) (uint16 fragCount) fragmentPayload
            fragments


    /// Parse a datagram header (old format without frag header).
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


    /// Parse a fragment datagram header.
    /// Returns Ok (msgType, clientId, requestId, fragIndex, fragCount, fragmentPayload) or Error () if too short.
    let tryParseFragmentHeader (data: byte[]) : Result<byte * VpnClientId * uint32 * uint16 * uint16 * byte[], unit> =
        let fullHeaderSize = HeaderSize + FragHeaderSize
        if data.Length < fullHeaderSize then
            Error ()
        else
            let msgType = data.[0]
            let guidBytes = data.[1..16]
            let clientId = Guid(guidBytes) |> VpnClientId
            let requestId = BitConverter.ToUInt32(data, 17)
            let fragIndex = BitConverter.ToUInt16(data, 21)
            let fragCount = BitConverter.ToUInt16(data, 23)
            let fragmentPayload =
                if data.Length > fullHeaderSize then
                    Array.sub data fullHeaderSize (data.Length - fullHeaderSize)
                else
                    [||]
            Ok (msgType, clientId, requestId, fragIndex, fragCount, fragmentPayload)


    /// Build an error response as fragmented datagrams.
    let buildErrorResponseFragments (clientId: VpnClientId) (requestId: uint32) (errorMsg: string) : byte[][] =
        let msgBytes = System.Text.Encoding.UTF8.GetBytes(errorMsg)
        let truncated = if msgBytes.Length > 1024 then msgBytes.[..1023] else msgBytes
        buildFragments MsgTypeErrorResponse clientId requestId truncated


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


    // === Reassembly types and logic ===

    /// Key for reassembly map: (msgType, clientId, requestId).
    type ReassemblyKey = byte * VpnClientId * uint32

    /// State for an in-progress reassembly.
    type ReassemblyState =
        {
            createdAtTicks : int64
            fragCount : uint16
            mutable receivedCount : int
            fragments : byte[][] // indexed by fragIndex
            mutable totalLength : int
        }

    /// Create a new reassembly state for the given fragCount.
    let createReassemblyState (createdAtTicks: int64) (fragCount: uint16) : ReassemblyState =
        {
            createdAtTicks = createdAtTicks
            fragCount = fragCount
            receivedCount = 0
            fragments = Array.zeroCreate (int fragCount)
            totalLength = 0
        }

    /// Try to add a fragment to the reassembly state.
    /// Returns Some(logicalPayload) if reassembly is complete, None otherwise.
    let tryAddFragment (state: ReassemblyState) (fragIndex: uint16) (fragmentPayload: byte[]) : byte[] option =
        let idx = int fragIndex
        if idx < 0 || idx >= state.fragments.Length then
            None // Invalid index - ignore.
        elif state.fragments.[idx] <> null then
            // Duplicate fragment - ignore but check if already complete.
            if state.receivedCount = int state.fragCount then
                // Already complete - reassemble again.
                let result = Array.zeroCreate state.totalLength
                let mutable offset = 0
                for i = 0 to state.fragments.Length - 1 do
                    let frag = state.fragments.[i]
                    Array.Copy(frag, 0, result, offset, frag.Length)
                    offset <- offset + frag.Length
                Some result
            else
                None
        else
            // New fragment.
            state.fragments.[idx] <- fragmentPayload
            state.receivedCount <- state.receivedCount + 1
            state.totalLength <- state.totalLength + fragmentPayload.Length

            if state.receivedCount = int state.fragCount then
                // All fragments received - reassemble.
                let result = Array.zeroCreate state.totalLength
                let mutable offset = 0
                for i = 0 to state.fragments.Length - 1 do
                    let frag = state.fragments.[i]
                    Array.Copy(frag, 0, result, offset, frag.Length)
                    offset <- offset + frag.Length
                Some result
            else
                None
