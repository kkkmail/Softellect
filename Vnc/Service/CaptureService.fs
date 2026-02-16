namespace Softellect.Vnc.Service

open System
open System.Drawing
open System.Net
open System.Net.Sockets
open System.Threading
open Softellect.Sys.Logging
open Softellect.Sys.Crypto
open Softellect.Transport.UdpProtocol
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.Protocol
open Softellect.Vnc.Interop

module CaptureService =

    /// Converts DXGI FrameData (C# interop) to F# FrameUpdate.
    let private toFrameUpdate (frameSeq: uint64) (frameData: FrameData) : FrameUpdate =
        let regions =
            frameData.DirtyRects
            |> Array.map (fun (r: Rectangle) ->
                let x = r.X
                let y = r.Y
                let w = r.Width
                let h = r.Height
                let stride = frameData.Stride
                let bytesPerPixel = 4
                let regionData = Array.zeroCreate (w * h * bytesPerPixel)
                for row in 0..h-1 do
                    let srcOffset = (y + row) * stride + x * bytesPerPixel
                    let dstOffset = row * w * bytesPerPixel
                    let copyLen = w * bytesPerPixel
                    if srcOffset + copyLen <= frameData.PixelData.Length then
                        Buffer.BlockCopy(frameData.PixelData, srcOffset, regionData, dstOffset, copyLen)
                {
                    x = x
                    y = y
                    width = w
                    height = h
                    data = regionData
                })

        let moveRegions =
            frameData.MoveRects
            |> Array.map (fun m ->
                {
                    x = m.DestinationRect.X
                    y = m.DestinationRect.Y
                    width = m.DestinationRect.Width
                    height = m.DestinationRect.Height
                    sourceX = m.SourcePoint.X
                    sourceY = m.SourcePoint.Y
                })

        {
            sequenceNumber = frameSeq
            screenWidth = frameData.Width
            screenHeight = frameData.Height
            regions = regions
            moveRegions = moveRegions
            cursorX = frameData.CursorPosition.X
            cursorY = frameData.CursorPosition.Y
            cursorShape = frameData.CursorShape |> Option.ofObj
        }


    type CaptureLoopState =
        {
            udpClient : UdpClient
            viewerEndpoint : IPEndPoint
            sessionId : PushSessionId
            mutable frameSequence : uint64
            sessionAesKey : byte[]
        }


    /// Runs the capture loop, sending frames via UDP to the viewer.
    /// Each chunk payload is encrypted with per-packet AES derived from the session key.
    let captureLoop (duplication: DesktopDuplication) (state: CaptureLoopState) (ct: CancellationToken) =

        while not ct.IsCancellationRequested do
            try
                match duplication.CaptureFrame(100) with
                | Ok frameData ->
                    if frameData.DirtyRects.Length > 0 || frameData.MoveRects.Length > 0 then
                        state.frameSequence <- state.frameSequence + 1UL
                        let frameUpdate = toFrameUpdate state.frameSequence frameData

                        match encodeFrameUpdate frameUpdate with
                        | Ok encoded ->
                            let chunks = chunkFrameData state.frameSequence encoded

                            for chunk in chunks do
                                let nonce = Guid.NewGuid()
                                let packetKey = derivePacketAesKey state.sessionAesKey nonce
                                match tryEncryptAesKey chunk packetKey with
                                | Ok encrypted ->
                                    let datagram = buildPushDatagram state.sessionId nonce encrypted
                                    state.udpClient.Send(datagram, datagram.Length, state.viewerEndpoint) |> ignore
                                | Error e ->
                                    Logger.logTrace (fun () -> $"CaptureLoop: Encrypt failed: %A{e}")
                        | Error e ->
                            Logger.logError $"CaptureLoop: Failed to encode frame: %A{e}"
                | Error err ->
                    if err = "access_lost" then
                        Logger.logWarn "CaptureLoop: DXGI access lost, reinitializing..."
                        match duplication.Reinitialize() with
                        | Ok _ -> Logger.logInfo "CaptureLoop: Reinitialize succeeded."
                        | Error e -> Logger.logError $"CaptureLoop: Reinitialize failed: {e}"
                    elif err <> "timeout" then
                        Logger.logTrace (fun () -> $"CaptureLoop: Frame capture: {err}")
            with
            | :? OperationCanceledException -> ()
            | ex -> Logger.logError $"CaptureLoop: Exception: {ex.Message}"
