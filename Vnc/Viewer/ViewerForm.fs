namespace Softellect.Vnc.Viewer

open System
open System.Drawing
open System.Net
open System.Net.Sockets
open System.Threading
open System.Windows.Forms
open Softellect.Sys.Crypto
open Softellect.Sys.Logging
open Softellect.Wcf.Common
open Softellect.Transport.UdpProtocol
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.Protocol
open Softellect.Vnc.Core.ServiceInfo
open Softellect.Vnc.Viewer.WcfClient
open Softellect.Vnc.Viewer.ScreenRenderer
open Softellect.Vnc.Viewer.InputCapture

module ViewerForm =

    /// Reassembles frame chunks received via UDP.
    type FrameReassembler() =
        let mutable currentFrameSeq = 0UL
        let mutable chunks : (int * byte[])[] = [||]
        let mutable totalChunks = 0
        let mutable receivedCount = 0

        member _.ProcessChunk(frameSeq: uint64, chunkIdx: int, total: int, data: byte[]) : byte[] option =
            if frameSeq > currentFrameSeq then
                currentFrameSeq <- frameSeq
                totalChunks <- total
                chunks <- Array.zeroCreate total
                receivedCount <- 0

            if frameSeq = currentFrameSeq && chunkIdx < totalChunks then
                let existing = fst chunks.[chunkIdx]
                if existing = 0 then
                    chunks.[chunkIdx] <- (1, data)
                    receivedCount <- receivedCount + 1

                    if receivedCount = totalChunks then
                        let totalLen = chunks |> Array.sumBy (fun (_, d) -> d.Length)
                        let result = Array.zeroCreate totalLen
                        let mutable offset = 0
                        for (_, d) in chunks do
                            Buffer.BlockCopy(d, 0, result, offset, d.Length)
                            offset <- offset + d.Length
                        Some result
                    else
                        None
                else
                    None
            else
                None


    type VncViewerForm(viewerData: VncViewerData, serviceAccessInfo: ServiceAccessInfo, localUdpPort: int) as this =
        inherit Form()

        let panel = new Panel(Dock = DockStyle.Fill, BackColor = Color.Black)
        let mutable screenBitmap : ScreenBitmap option = None
        let mutable wcfClient : VncWcfClient option = None
        let mutable sessionId : VncSessionId option = None
        let mutable sessionAesKey : byte[] option = None
        let mutable receiverThread : Thread option = None
        let mutable receiverCts : CancellationTokenSource option = None
        let mutable remoteWidth = 0
        let mutable remoteHeight = 0
        let reassembler = FrameReassembler()

        let startUdpReceiver () =
            let cts = new CancellationTokenSource()
            receiverCts <- Some cts

            let thread = Thread(fun () ->
                use udpClient = new UdpClient(localUdpPort)
                udpClient.Client.ReceiveTimeout <- 1000

                Logger.logInfo $"UDP receiver started on port {localUdpPort}"

                while not cts.Token.IsCancellationRequested do
                    try
                        let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                        let data = udpClient.Receive(&remoteEp)

                        match tryParsePushDatagram data with
                        | Ok (_, nonce, encryptedPayload) ->
                            // Decrypt with per-packet AES key derived from session key + nonce
                            match sessionAesKey with
                            | Some key ->
                                let packetKey = derivePacketAesKey key nonce
                                match tryDecryptAesKey encryptedPayload packetKey with
                                | Ok payload ->
                                    match tryParseFrameChunk payload with
                                    | Ok (frameSeq, chunkIdx, total, chunkData) ->
                                        match reassembler.ProcessChunk(frameSeq, chunkIdx, total, chunkData) with
                                        | Some encodedFrame ->
                                            match decodeFrameUpdate encodedFrame with
                                            | Ok frame ->
                                                match screenBitmap with
                                                | Some sb ->
                                                    sb.ApplyFrame(frame)
                                                    try this.BeginInvoke(Action(fun () -> panel.Invalidate())) |> ignore
                                                    with | _ -> ()
                                                | None -> ()
                                            | Error e ->
                                                Logger.logTrace (fun () -> $"Frame decode error: %A{e}")
                                        | None -> ()
                                    | Error _ -> ()
                                | Error _ -> ()
                            | None -> ()
                        | Error _ -> ()
                    with
                    | :? SocketException -> ()
                    | :? OperationCanceledException -> ()
                    | ex -> Logger.logTrace (fun () -> $"UDP receiver error: {ex.Message}")

                Logger.logInfo "UDP receiver stopped"
            )
            thread.IsBackground <- true
            thread.Name <- "VncUdpReceiver"
            thread.Start()
            receiverThread <- Some thread

        let connectToService () =
            try
                let client = VncWcfClient(viewerData, serviceAccessInfo)
                wcfClient <- Some client

                let localIp =
                    match serviceAccessInfo with
                    | NetTcpServiceInfo info ->
                        use tempSocket = new UdpClient()
                        tempSocket.Connect(info.netTcpServiceAddress.value.value, 1)
                        (tempSocket.Client.LocalEndPoint :?> IPEndPoint).Address.ToString()
                    | HttpServiceInfo info ->
                        use tempSocket = new UdpClient()
                        tempSocket.Connect(info.httpServiceAddress.value.value, 1)
                        (tempSocket.Client.LocalEndPoint :?> IPEndPoint).Address.ToString()

                let request : VncConnectRequest =
                    {
                        viewerId = viewerData.viewerId
                        viewerUdpAddress = localIp
                        viewerUdpPort = localUdpPort
                        timestamp = DateTime.UtcNow
                    }

                Logger.logInfo $"Connecting to VNC service as viewer {viewerData.viewerId.value}, at {localIp}:{localUdpPort}..."

                match client.connect request with
                | Ok response ->
                    sessionId <- Some response.sessionId
                    sessionAesKey <- Some response.sessionAesKey
                    remoteWidth <- response.screenWidth
                    remoteHeight <- response.screenHeight

                    let sb = new ScreenBitmap(remoteWidth, remoteHeight)
                    screenBitmap <- Some sb

                    this.Text <- $"VNC Viewer - {remoteWidth}x{remoteHeight}"
                    Logger.logInfo $"Connected: session={response.sessionId.value}, screen={remoteWidth}x{remoteHeight}"

                    startUdpReceiver ()
                | Error e ->
                    Logger.logError $"Connect failed: %A{e}"
                    MessageBox.Show($"Failed to connect: %A{e}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore
            with
            | ex ->
                Logger.logError $"Connection exception: {ex.Message}"
                MessageBox.Show($"Connection error: {ex.Message}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore

        do
            this.Text <- "VNC Viewer"
            this.Size <- Size(1280, 720)
            this.KeyPreview <- true

            let ty = typeof<Panel>
            let mi = ty.GetMethod("SetStyle", Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic)
            mi.Invoke(panel, [| ControlStyles.AllPaintingInWmPaint ||| ControlStyles.UserPaint ||| ControlStyles.DoubleBuffer :> obj; true :> obj |]) |> ignore

            this.Controls.Add(panel)

            // Wire panel paint
            panel.Paint.Add(fun e ->
                match screenBitmap with
                | Some sb ->
                    let destRect = Rectangle(0, 0, panel.Width, panel.Height)
                    sb.DrawTo(e.Graphics, destRect)
                | None -> ()
            )

            // Wire mouse events
            panel.MouseMove.Add(fun e ->
                match wcfClient with
                | Some client when remoteWidth > 0 ->
                    sendMouseEvent client remoteWidth remoteHeight panel.Width panel.Height e "move"
                | _ -> ()
            )

            panel.MouseDown.Add(fun e ->
                match wcfClient with
                | Some client when remoteWidth > 0 ->
                    sendMouseEvent client remoteWidth remoteHeight panel.Width panel.Height e "down"
                | _ -> ()
            )

            panel.MouseUp.Add(fun e ->
                match wcfClient with
                | Some client when remoteWidth > 0 ->
                    sendMouseEvent client remoteWidth remoteHeight panel.Width panel.Height e "up"
                | _ -> ()
            )

            panel.MouseWheel.Add(fun e ->
                match wcfClient with
                | Some client when remoteWidth > 0 ->
                    sendMouseEvent client remoteWidth remoteHeight panel.Width panel.Height e "wheel"
                | _ -> ()
            )

            // Wire keyboard events
            this.KeyDown.Add(fun e ->
                match wcfClient with
                | Some client ->
                    sendKeyEvent client e true
                    e.Handled <- true
                | None -> ()
            )

            this.KeyUp.Add(fun e ->
                match wcfClient with
                | Some client ->
                    sendKeyEvent client e false
                    e.Handled <- true
                | None -> ()
            )

        override _.OnLoad(e) =
            base.OnLoad(e)
            connectToService ()

        override _.OnFormClosing(e) =
            base.OnFormClosing(e)

            match receiverCts with
            | Some cts -> cts.Cancel(); cts.Dispose()
            | None -> ()

            match sessionId, wcfClient with
            | Some sid, Some client ->
                try client.disconnect sid |> ignore
                with | _ -> ()
            | _ -> ()

            match screenBitmap with
            | Some sb -> (sb :> IDisposable).Dispose()
            | None -> ()
