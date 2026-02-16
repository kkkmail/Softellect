namespace Softellect.Vnc.Core

open System
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.Errors
open Softellect.Transport.UdpProtocol

module Protocol =

    let private serializationFormat = BinaryZippedFormat

    /// Encode a FrameUpdate to a compressed byte array for transport.
    let encodeFrameUpdate (frame: FrameUpdate) : Result<byte[], VncError> =
        match trySerialize serializationFormat frame with
        | Ok bytes -> Ok bytes
        | Error e -> Error (VncCaptureErr (EncodingErr $"Failed to encode frame: %A{e}"))

    /// Decode a FrameUpdate from a compressed byte array.
    let decodeFrameUpdate (data: byte[]) : Result<FrameUpdate, VncError> =
        match tryDeserialize<FrameUpdate> serializationFormat data with
        | Ok frame -> Ok frame
        | Error e -> Error (VncCaptureErr (EncodingErr $"Failed to decode frame: %A{e}"))

    /// Encode an InputEvent for transport.
    let encodeInputEvent (event: InputEvent) : Result<byte[], VncError> =
        match trySerialize serializationFormat event with
        | Ok bytes -> Ok bytes
        | Error e -> Error (VncInputErr (SendInputErr $"Failed to encode input event: %A{e}"))

    /// Decode an InputEvent from bytes.
    let decodeInputEvent (data: byte[]) : Result<InputEvent, VncError> =
        match tryDeserialize<InputEvent> serializationFormat data with
        | Ok event -> Ok event
        | Error e -> Error (VncInputErr (SendInputErr $"Failed to decode input event: %A{e}"))

    /// Encode ClipboardData for transport.
    let encodeClipboardData (clip: ClipboardData) : Result<byte[], VncError> =
        match trySerialize serializationFormat clip with
        | Ok bytes -> Ok bytes
        | Error e -> Error (VncGeneralErr $"Failed to encode clipboard data: %A{e}")

    /// Decode ClipboardData from bytes.
    let decodeClipboardData (data: byte[]) : Result<ClipboardData, VncError> =
        match tryDeserialize<ClipboardData> serializationFormat data with
        | Ok clip -> Ok clip
        | Error e -> Error (VncGeneralErr $"Failed to decode clipboard data: %A{e}")

    // === Frame chunking for UDP transport ===

    /// Frame chunk header: 8 (sequence) + 2 (chunkIndex) + 2 (totalChunks) = 12 bytes.
    [<Literal>]
    let FrameChunkHeaderSize = 12

    /// Maximum chunk data size within a push datagram payload.
    let FrameChunkMaxData = PushMaxPayload - FrameChunkHeaderSize

    /// Build a frame chunk payload (to be wrapped in buildPushDatagram).
    let buildFrameChunk (frameSeq: uint64) (chunkIndex: int) (totalChunks: int) (data: byte[]) : byte[] =
        let result = Array.zeroCreate (FrameChunkHeaderSize + data.Length)
        BitConverter.GetBytes(frameSeq) |> fun b -> Buffer.BlockCopy(b, 0, result, 0, 8)
        BitConverter.GetBytes(uint16 chunkIndex) |> fun b -> Buffer.BlockCopy(b, 0, result, 8, 2)
        BitConverter.GetBytes(uint16 totalChunks) |> fun b -> Buffer.BlockCopy(b, 0, result, 10, 2)
        Buffer.BlockCopy(data, 0, result, FrameChunkHeaderSize, data.Length)
        result

    /// Parse a frame chunk payload.
    let tryParseFrameChunk (payload: byte[]) : Result<uint64 * int * int * byte[], string> =
        if payload.Length < FrameChunkHeaderSize then
            Error "Chunk payload too short"
        else
            let frameSeq = BitConverter.ToUInt64(payload, 0)
            let chunkIndex = int (BitConverter.ToUInt16(payload, 8))
            let totalChunks = int (BitConverter.ToUInt16(payload, 10))
            let data = Array.sub payload FrameChunkHeaderSize (payload.Length - FrameChunkHeaderSize)
            Ok (frameSeq, chunkIndex, totalChunks, data)

    /// Split encoded frame data into chunk payloads for UDP transport.
    let chunkFrameData (frameSeq: uint64) (encodedFrame: byte[]) : byte[][] =
        let totalChunks = max 1 ((encodedFrame.Length + FrameChunkMaxData - 1) / FrameChunkMaxData)
        [| for i in 0..totalChunks-1 do
            let offset = i * FrameChunkMaxData
            let length = min FrameChunkMaxData (encodedFrame.Length - offset)
            let data = Array.sub encodedFrame offset length
            buildFrameChunk frameSeq i totalChunks data |]
