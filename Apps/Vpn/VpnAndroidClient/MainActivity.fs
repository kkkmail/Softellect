namespace Softellect.Vpn.Android

open System
open System.IO
open System.Threading
open Android.App
open Android.Content
open Android.OS
open Android.Widget
open Android.Views
open Android.Net
open Android.Graphics
open Softellect.Sys.Logging
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.AndroidClient
open Softellect.Vpn.AndroidClient.ConfigManager

/// VPN connection states for UI display.
type VpnConnectionState =
    | Disconnected
    | Connecting
    | Connected


/// Request codes for activity results.
module RequestCodes =
    [<Literal>]
    let VpnPermission = 1001

    [<Literal>]
    let ImportConfig = 1002


/// Main activity for the VPN Android client.
[<Activity(Label = "Softellect VPN", MainLauncher = true, Theme = "@style/AppTheme")>]
type MainActivity() =
    inherit Activity()

    let mutable connectionState = Disconnected
    let mutable startStopButton: Button = null
    let mutable importConfigButton: Button = null
    let mutable statusText: TextView = null
    let mutable serverInfoText: TextView = null
    let mutable statsText: TextView = null
    let mutable vpnService: VpnTunnelServiceImpl option = None
    let mutable serviceData: VpnClientServiceData option = None
    let mutable statsTimer: Timer = null

    // Connection info
    let mutable serverHost = "not configured"
    let mutable basicHttpPort = 0
    let mutable udpPort = 0
    let mutable sessionId = ""

    // Stats counters
    let mutable bytesSent = 0L
    let mutable bytesReceived = 0L
    let mutable packetsSent = 0L
    let mutable packetsReceived = 0L

    //// Config file path in app-private storage
    //let getConfigFilePath (context: Context) =
    //    Path.Combine(context.FilesDir.AbsolutePath, "vpn_config.json")

    let shortenSessionId (sid: string) =
        if String.IsNullOrEmpty(sid) then "N/A"
        elif sid.Length <= 8 then sid
        else sid.Substring(0, 8) + "..."

    member private this.UpdateUI() =
        this.RunOnUiThread(fun () ->
            match connectionState with
            | Disconnected ->
                startStopButton.Text <- "START"
                startStopButton.SetBackgroundColor(Color.ParseColor("#CC0000")) // Red
                statusText.Text <- "Disconnected"
                statusText.SetTextColor(Color.ParseColor("#CC0000"))
                importConfigButton.Enabled <- true
            | Connecting ->
                startStopButton.Text <- "CONNECTING..."
                startStopButton.SetBackgroundColor(Color.ParseColor("#CCAA00")) // Yellow/Orange
                statusText.Text <- "Connecting..."
                statusText.SetTextColor(Color.ParseColor("#CCAA00"))
                importConfigButton.Enabled <- false
            | Connected ->
                startStopButton.Text <- "STOP"
                startStopButton.SetBackgroundColor(Color.ParseColor("#00CC00")) // Green
                statusText.Text <- "Connected"
                statusText.SetTextColor(Color.ParseColor("#00CC00"))
                importConfigButton.Enabled <- false

            startStopButton.Enabled <- serviceData.IsSome

            serverInfoText.Text <- $"Server: {serverHost}\nBasicHttp Port: {basicHttpPort}\nUDP Port: {udpPort}\nSession: {shortenSessionId sessionId}"
            statsText.Text <- $"Sent: {bytesSent} bytes ({packetsSent} packets)\nReceived: {bytesReceived} bytes ({packetsReceived} packets)"
        )

    member private this.UpdateStats() =
        match vpnService with
        | Some svc when svc.IsRunning ->
            let (sent, recv, pktSent, pktRecv) = svc.GetStats()
            bytesSent <- sent
            bytesReceived <- recv
            packetsSent <- pktSent
            packetsReceived <- pktRecv
            this.UpdateUI()
        | _ -> ()

    member private this.StartStatsTimer() =
        if statsTimer <> null then
            statsTimer.Dispose()
        statsTimer <- new Timer(
            (fun _ -> this.UpdateStats()),
            null,
            TimeSpan.FromSeconds(1.0),
            TimeSpan.FromSeconds(1.0))

    member private this.StopStatsTimer() =
        if statsTimer <> null then
            statsTimer.Dispose()
            statsTimer <- null

    member private this.LoadConfig() =
        match tryLoadConfigFromFile this with
        | Ok config ->
            serverHost <- config.serverHost
            basicHttpPort <- config.basicHttpPort
            udpPort <- config.udpPort
            match toVpnClientServiceData config with
            | Ok data ->
                serviceData <- Some data
                Logger.logInfo $"Config loaded: {serverHost}:{basicHttpPort}"
            | Error e ->
                Logger.logError $"Failed to convert config: {e}"
                serviceData <- None
        | Error e ->
            Logger.logError $"Failed to load config: {e}"
            serviceData <- None

    //member private this.SaveConfig(json: string) =
    //    try
    //        let configPath = getConfigFilePath this
    //        File.WriteAllText(configPath, json)
    //        Logger.logInfo $"Config saved to {configPath}"
    //        true
    //    with
    //    | ex ->
    //        Logger.logError $"Failed to save config: {ex.Message}"
    //        false

    member private this.OnImportConfigClick() =
        let intent = new Intent(Intent.ActionOpenDocument)
        intent.AddCategory(Intent.CategoryOpenable) |> ignore
        intent.SetType("application/json") |> ignore
        this.StartActivityForResult(intent, RequestCodes.ImportConfig)

    member private this.RequestVpnPermission() =
        let intent = VpnService.Prepare(this)
        if intent <> null then
            this.StartActivityForResult(intent, RequestCodes.VpnPermission)
        else
            // Already have permission
            this.StartVpnConnection()

    member private this.StartVpnConnection() =
        match serviceData with
        | Some data ->
            async {
                try
                    let svc = new VpnTunnelServiceImpl()
                    vpnService <- Some svc
                    if svc.StartVpn(data) then
                        connectionState <- Connected
                        this.StartStatsTimer()
                    else
                        connectionState <- Disconnected
                        vpnService <- None
                with
                | ex ->
                    Logger.logError $"VPN start failed: {ex.Message}"
                    connectionState <- Disconnected
                    vpnService <- None
                this.UpdateUI()
            } |> Async.Start
        | None ->
            Toast.MakeText(this, "No config loaded", ToastLength.Short).Show()
            connectionState <- Disconnected
            this.UpdateUI()

    member private this.StopVpnConnection() =
        this.StopStatsTimer()
        match vpnService with
        | Some svc ->
            svc.StopVpn()
            vpnService <- None
        | None -> ()
        sessionId <- ""
        bytesSent <- 0L
        bytesReceived <- 0L
        packetsSent <- 0L
        packetsReceived <- 0L
        connectionState <- Disconnected
        this.UpdateUI()

    member private this.OnStartStopClick() =
        match connectionState with
        | Disconnected ->
            connectionState <- Connecting
            this.UpdateUI()
            this.RequestVpnPermission()
        | Connecting ->
            // Cancel connection attempt
            this.StopVpnConnection()
        | Connected ->
            // Stop VPN
            this.StopVpnConnection()

    override this.OnActivityResult(requestCode: int, resultCode: Result, data: Intent) =
        base.OnActivityResult(requestCode, resultCode, data)

        match requestCode with
        | RequestCodes.VpnPermission ->
            if resultCode = Result.Ok then
                this.StartVpnConnection()
            else
                Toast.MakeText(this, "VPN permission denied", ToastLength.Short).Show()
                connectionState <- Disconnected
                this.UpdateUI()

        | RequestCodes.ImportConfig ->
            if resultCode = Result.Ok && data <> null && data.Data <> null then
                try
                    //use stream = this.ContentResolver.OpenInputStream(data.Data)
                    //use reader = new StreamReader(stream)
                    //let json = reader.ReadToEnd()
                    //if this.SaveConfig(json) then
                    //    this.LoadConfig()
                    //    this.UpdateUI()
                    //    Toast.MakeText(this, "Config imported successfully", ToastLength.Short).Show()
                    //else
                    //    Toast.MakeText(this, "Failed to save config", ToastLength.Short).Show()
                    this.LoadConfig()
                    this.UpdateUI()
                with
                | ex ->
                    Logger.logError $"Import failed: {ex.Message}"
                    Toast.MakeText(this, $"Import failed: {ex.Message}", ToastLength.Long).Show()
        | _ -> ()

    override this.OnCreate(savedInstanceState: Bundle) =
        base.OnCreate(savedInstanceState)

        // Create layout programmatically
        let layout = new LinearLayout(this)
        layout.Orientation <- Orientation.Vertical
        layout.SetPadding(32, 32, 32, 32)

        // Status text
        statusText <- new TextView(this)
        statusText.TextSize <- 24.0f
        statusText.Gravity <- GravityFlags.Center
        let statusParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            LinearLayout.LayoutParams.WrapContent)
        statusParams.BottomMargin <- 32
        statusText.LayoutParameters <- statusParams
        layout.AddView(statusText)

        // Start/Stop button
        startStopButton <- new Button(this)
        startStopButton.TextSize <- 32.0f
        let buttonParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            300)
        buttonParams.BottomMargin <- 16
        startStopButton.LayoutParameters <- buttonParams
        startStopButton.Click.Add(fun _ -> this.OnStartStopClick())
        layout.AddView(startStopButton)

        // Import Config button
        importConfigButton <- new Button(this)
        importConfigButton.Text <- "IMPORT CONFIG"
        importConfigButton.TextSize <- 16.0f
        let importParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            LinearLayout.LayoutParams.WrapContent)
        importParams.BottomMargin <- 32
        importConfigButton.LayoutParameters <- importParams
        importConfigButton.Click.Add(fun _ -> this.OnImportConfigClick())
        layout.AddView(importConfigButton)

        // Server info text
        serverInfoText <- new TextView(this)
        serverInfoText.TextSize <- 14.0f
        let serverInfoParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            LinearLayout.LayoutParams.WrapContent)
        serverInfoParams.BottomMargin <- 16
        serverInfoText.LayoutParameters <- serverInfoParams
        layout.AddView(serverInfoText)

        // Stats text
        statsText <- new TextView(this)
        statsText.TextSize <- 14.0f
        let statsParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            LinearLayout.LayoutParams.WrapContent)
        statsText.LayoutParameters <- statsParams
        layout.AddView(statsText)

        this.SetContentView(layout)

        // Load existing config
        this.LoadConfig()
        this.UpdateUI()

    override this.OnDestroy() =
        this.StopStatsTimer()
        match vpnService with
        | Some svc -> svc.StopVpn()
        | None -> ()
        base.OnDestroy()
