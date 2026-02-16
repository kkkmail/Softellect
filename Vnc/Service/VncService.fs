namespace Softellect.Vnc.Service

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Softellect.Sys.Logging
open Softellect.Transport.UdpProtocol
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.Errors
open Softellect.Vnc.Core.CryptoHelpers
open Softellect.Vnc.Core.ServiceInfo
open Softellect.Vnc.Service.CaptureService
open Softellect.Vnc.Service.InputService
open Softellect.Vnc.Interop

module VncServiceImpl =

    type VncService(data: VncServerData) =
        let mutable started = false
        let mutable captureCts : CancellationTokenSource option = None
        let mutable captureThread : Thread option = None
        let mutable duplication : DesktopDuplication option = None
        let mutable udpClient : UdpClient option = None
        let mutable currentSession : VncSessionId option = None
        let mutable currentSessionAesKey : byte[] option = None
        let syncLock = obj()

        let initDuplication () =
            match DesktopDuplication.Create(0) with
            | Ok dup ->
                duplication <- Some dup
                Ok ()
            | Error e ->
                Error (VncCaptureErr (DxgiInitErr e))

        let startCapture (viewerAddress: string) (viewerUdpPort: int) (sessionAesKey: byte[]) =
            lock syncLock (fun () ->
                match duplication with
                | Some dup ->
                    let client = new UdpClient(data.vncServiceAccessInfo.udpPort)
                    udpClient <- Some client
                    let viewerEp = IPEndPoint(IPAddress.Parse(viewerAddress), viewerUdpPort)

                    let cts = new CancellationTokenSource()
                    captureCts <- Some cts

                    let state =
                        {
                            udpClient = client
                            viewerEndpoint = viewerEp
                            sessionId = PushSessionId 1uy
                            frameSequence = 0UL
                            sessionAesKey = sessionAesKey
                        }

                    let thread = Thread(fun () ->
                        try
                            captureLoop dup state cts.Token
                        with
                        | ex -> Logger.logError $"Capture thread exception: {ex.Message}"
                    )
                    thread.IsBackground <- true
                    thread.Name <- "VncCaptureThread"
                    thread.Start()
                    captureThread <- Some thread

                    Logger.logInfo $"Capture started, sending frames to {viewerAddress}:{viewerUdpPort}"
                    Ok ()
                | None ->
                    Error (VncCaptureErr (DxgiInitErr "DesktopDuplication not initialized"))
            )

        let stopCapture () =
            lock syncLock (fun () ->
                match captureCts with
                | Some cts ->
                    cts.Cancel()
                    match captureThread with
                    | Some t when t.IsAlive -> t.Join(2000) |> ignore
                    | _ -> ()
                    cts.Dispose()
                    captureCts <- None
                    captureThread <- None
                | None -> ()

                match udpClient with
                | Some c ->
                    c.Close()
                    udpClient <- None
                | None -> ()

                currentSession <- None
                currentSessionAesKey <- None
                Logger.logInfo "Capture stopped."
            )

        member _.sessionAesKey = currentSessionAesKey

        interface IVncService with
            member _.connect request =
                Logger.logInfo $"Viewer {request.viewerId.value} connecting from {request.viewerUdpAddress}:{request.viewerUdpPort}"

                stopCapture()

                match initDuplication() with
                | Ok () ->
                    let sessionId = VncSessionId.create()
                    let sessionAesKey = generateSessionAesKey()
                    currentSession <- Some sessionId
                    currentSessionAesKey <- Some sessionAesKey

                    match startCapture request.viewerUdpAddress request.viewerUdpPort sessionAesKey with
                    | Ok () ->
                        let w = match duplication with | Some d -> d.Width | None -> 0
                        let h = match duplication with | Some d -> d.Height | None -> 0

                        Ok
                            {
                                sessionId = sessionId
                                screenWidth = w
                                screenHeight = h
                                sessionAesKey = sessionAesKey
                            }
                    | Error e -> Error e
                | Error e -> Error e

            member _.disconnect sessionId =
                Logger.logInfo $"Viewer disconnecting, sessionId: {sessionId.value}"
                match currentSession with
                | Some sid when sid = sessionId ->
                    stopCapture()
                    Ok ()
                | _ ->
                    Logger.logWarn "disconnect: session mismatch or no active session"
                    Ok ()

            member _.sendInput event =
                processInputEvent event

            member _.getClipboard () =
                match ClipboardInterop.GetClipboardContent() with
                | Ok clipData -> Ok clipData
                | Error e -> Error (VncInputErr (ClipboardErr e))

            member _.setClipboard data =
                match ClipboardInterop.SetClipboardContent(data) with
                | Ok _ -> Ok ()
                | Error e -> Error (VncInputErr (ClipboardErr e))

        interface IHostedService with
            member _.StartAsync(_cancellationToken: CancellationToken) =
                if started then
                    Logger.logInfo "VNC Service already started"
                    Task.CompletedTask
                else
                    Logger.logInfo "Starting VNC Service..."
                    started <- true
                    Logger.logInfo $"VNC Service started. WCF on {data.vncServiceAccessInfo.serviceAccessInfo.getUrl()}, UDP port {data.vncServiceAccessInfo.udpPort}"
                    Task.CompletedTask

            member _.StopAsync(_cancellationToken: CancellationToken) =
                if not started then
                    Logger.logInfo "VNC Service already stopped"
                    Task.CompletedTask
                else
                    Logger.logInfo "Stopping VNC Service..."
                    stopCapture()
                    match duplication with
                    | Some d -> d.Dispose(); duplication <- None
                    | None -> ()
                    started <- false
                    Logger.logInfo "VNC Service stopped"
                    Task.CompletedTask
