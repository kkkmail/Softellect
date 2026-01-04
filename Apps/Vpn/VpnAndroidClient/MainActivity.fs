namespace Softellect.Vpn.Android

open System
open System.Diagnostics
open System.IO
open System.Threading
open Android.App
open Android.Content
open Android.Content.Res
open Android.OS
open Android.Widget
open Android.Views
open Android.Net
open Android.Graphics
open Android.Graphics.Drawables
open Android.Text
open Softellect.Sys.Primitives
open Softellect.Sys.Logging
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.AndroidClient
open Softellect.Vpn.AndroidClient.ConfigManager
open Softellect.Vpn.AndroidClient.LogBuffer
open Softellect.Vpn.Client.WcfClient
open Android.Content.PM
open VpnAndroidClient


/// VPN connection states for UI display (spec 050/057: includes Reconnecting and VersionMismatch).
type VpnConnectionState =
    | Disconnected
    | Connecting
    | Connected
    | Reconnecting
    | VersionMismatch  // Spec 057: Version mismatch (WARN or ERROR)


/// Request codes for activity results.
module RequestCodes =
    [<Literal>]
    let VpnPermission = 1001


/// Pastel colors for button states (muted, not saturated).
module PastelColors =
    let paleRed() = Color.ParseColor("#E57373")       // Disconnected
    let paleYellow() = Color.ParseColor("#FFD54F")    // Connecting
    let paleGreen() = Color.ParseColor("#81C784")     // Connected
    let paleOrange() = Color.ParseColor("#FFB74D")    // Reconnecting (spec 050)
    let palePurple() = Color.ParseColor("#BA68C8")    // Disconnecting (spec 055)
    let textRed() = Color.ParseColor("#C62828")       // Darker red for text
    let textYellow() = Color.ParseColor("#F57F17")    // Darker yellow for text
    let textGreen() = Color.ParseColor("#2E7D32")     // Darker green for text
    let textOrange() = Color.ParseColor("#E65100")    // Darker orange for text (spec 050)
    let textPurple() = Color.ParseColor("#7B1FA2")    // Darker purple for text (spec 055)


/// Spec 057: Title bar colors for version states.
module TitleBarColors =
    let normal() = Color.ParseColor("#3F51B5")        // Primary color (default bluish)
    let versionWarn() = Color.ParseColor("#FFC107")   // Yellowish (amber) for version WARN
    let versionError() = Color.ParseColor("#D32F2F")  // Reddish for version ERROR


/// Spec 057: Version state for UI display.
type VersionState =
    | VersionOkState
    | VersionWarnState
    | VersionErrorState


/// Main activity for the VPN Android client.
[<Activity(Label = "Softellect VPN", MainLauncher = true, Theme = "@style/AppTheme",
           ConfigurationChanges = (ConfigChanges.Orientation ||| ConfigChanges.ScreenSize))>]
type MainActivity() =
    inherit Activity()

    let mutable connectionState = Disconnected
    let mutable pendingDisconnect = false // Spec 055: UI-only flag for immediate feedback
    let mutable powerButton: ImageButton = null
    let mutable statusText: TextView = null
    let mutable infoPaneText: TextView = null
    let mutable logPaneText: TextView = null
    let mutable logScrollView: ScrollView = null
    let mutable titleBar: LinearLayout = null  // Spec 057: Reference to title bar for color changes
    let mutable vpnService: VpnTunnelServiceImpl option = None
    let mutable serviceData: VpnClientServiceData option = None
    let mutable statsTimer: Timer = null
    let mutable configLoadError: string option = None
    let mutable lastError: string = ""
    let mutable versionState: VersionState = VersionOkState  // Spec 057: Version state for UI
    let mutable versionInfo: VersionCheckInfo option = None  // Spec 057: Version info for display

    // Connection info from config
    let mutable serverHost = ""
    let mutable basicHttpPort = 0
    let mutable udpPort = 0
    let mutable clientId = ""
    let mutable serverId = ""
    let mutable sessionId: byte = 0uy

    // Stats counters
    let mutable bytesSent = 0L
    let mutable bytesReceived = 0L
    let mutable packetsSent = 0L
    let mutable packetsReceived = 0L

    // Auto-scroll tracking for log pane
    let mutable userScrolledUp = false


    /// Configure the Logger to write to both console and LogBuffer.
    let configureLogging() =
        let stopwatch = Stopwatch.StartNew()
        let logGate = obj()
        Logger.configureLogger (fun level message callerName ->
            let elapsedSeconds = double stopwatch.ElapsedMilliseconds / 1_000.0
            let ts = DateTime.Now
            let s = ts.ToString("HH:mm:ss.fff")
            let line = $"{s} [{level.logName}] {callerName}: %A{message}"
            // Write to console
            lock logGate (fun () -> Console.WriteLine(line))
            // Write to LogBuffer for UI display
            LogBuffer.addLine line
        )


    /// Set the last error and log it.
    member private this.SetLastError(error: string) =
        lastError <- error
        Logger.logError error


    /// Format session ID as decimal NN (since it's 1 byte).
    member private this.FormatSessionId() =
        if sessionId = 0uy then "N/A"
        else $"{int sessionId}"


    /// Get current network info for Info pane.
    member private this.GetNetworkInfo() =
        try
            let netType = getNetworkType().ToDisplayString()
            let ifName = getPhysicalInterfaceName()
            let gateway = getPhysicalGatewayIp()
            let gwStr = match gateway with Ip4 ip -> ip | Ip6 ip -> ip
            (netType, ifName, gwStr)
        with
        | ex ->
            this.SetLastError $"Network info error: {ex.Message}"
            ("Unknown", "N/A", "N/A")


    /// Build the Info pane content.
    member private this.BuildInfoPaneText() =
        let sb = System.Text.StringBuilder()

        // Configuration section
        sb.AppendLine("── Configuration ──") |> ignore
        sb.AppendLine($"""Server: {(if String.IsNullOrEmpty serverHost then "\"not configured\"" else serverHost)}""") |> ignore
        sb.AppendLine($"BasicHttp Port: {basicHttpPort}") |> ignore
        sb.AppendLine($"UDP Port: {udpPort}") |> ignore
        sb.AppendLine($"ClientId: {clientId}") |> ignore
        sb.AppendLine($"ServerId: {serverId}") |> ignore
        sb.AppendLine() |> ignore

        // Spec 057: Version section
        sb.AppendLine("── Version ──") |> ignore
        match versionInfo with
        | Some info ->
            sb.AppendLine($"Client build: {info.clientBuild}") |> ignore
            sb.AppendLine($"Server build: {info.serverBuild}") |> ignore
            sb.AppendLine($"Min client (server): {info.minAllowedClientByServer}") |> ignore
            sb.AppendLine($"Min server (client): {info.minAllowedServerByClient}") |> ignore
            let statusStr =
                match versionState with
                | VersionOkState -> "OK"
                | VersionWarnState -> "WARN"
                | VersionErrorState -> "ERROR"
            sb.AppendLine($"Status: {statusStr}") |> ignore
        | None ->
            sb.AppendLine("Not checked yet") |> ignore
        sb.AppendLine() |> ignore

        // Session section
        sb.AppendLine("── Session ──") |> ignore
        sb.AppendLine($"SessionId: {this.FormatSessionId()}") |> ignore
        sb.AppendLine() |> ignore

        // Traffic section
        sb.AppendLine("── Traffic ──") |> ignore
        sb.AppendLine($"Bytes sent: {bytesSent:N0}") |> ignore
        sb.AppendLine($"Bytes received: {bytesReceived:N0}") |> ignore
        sb.AppendLine() |> ignore

        // Network section
        let (netType, ifName, gateway) = this.GetNetworkInfo()
        sb.AppendLine("── Network ──") |> ignore
        sb.AppendLine($"Type: {netType}") |> ignore
        sb.AppendLine($"Interface: {ifName}") |> ignore
        sb.AppendLine($"Gateway: {gateway}") |> ignore
        sb.AppendLine() |> ignore

        // Errors section
        sb.AppendLine("── Errors ──") |> ignore
        sb.AppendLine($"""Last error: {(if String.IsNullOrEmpty lastError then "\"None\"" else lastError)}""") |> ignore

        sb.ToString()


    /// Update the power button appearance based on state.
    member private this.UpdatePowerButton() =
        if isNull powerButton then () else
        let (bgColor, enabled) =
            match configLoadError with
            | Some _ -> (PastelColors.paleRed(), false) // Fatal config error - disabled
            | None ->
                // Spec 055: Check pending disconnect first for immediate UI feedback
                if pendingDisconnect then
                    (PastelColors.palePurple(), true)
                else
                    match connectionState with
                    | Disconnected -> (PastelColors.paleRed(), serviceData.IsSome)
                    | Connecting -> (PastelColors.paleYellow(), true)
                    | Connected -> (PastelColors.paleGreen(), true)
                    | Reconnecting -> (PastelColors.paleOrange(), true) // Spec 050
                    | VersionMismatch -> (PastelColors.paleRed(), false) // Spec 057: Disabled on version mismatch

        // Create round drawable background
        let drawable = new GradientDrawable()
        drawable.SetShape(ShapeType.Oval)
        drawable.SetColor(bgColor)
        powerButton.Background <- drawable
        powerButton.Enabled <- enabled


    /// Update the status text based on state.
    member private this.UpdateStatusText() =
        if isNull statusText then () else
        let (text, color) =
            match configLoadError with
            | Some _ -> ("Config Error", PastelColors.textRed())
            | None ->
                // Spec 055: Check pending disconnect first for immediate UI feedback
                if pendingDisconnect then
                    ("Disconnecting…", PastelColors.textPurple())
                else
                    match connectionState with
                    | Disconnected -> ("Disconnected", PastelColors.textRed())
                    | Connecting -> ("Connecting…", PastelColors.textYellow())
                    | Connected -> ("Connected", PastelColors.textGreen())
                    | Reconnecting -> ("Reconnecting…", PastelColors.textOrange()) // Spec 050
                    | VersionMismatch -> ("Version Mismatch", PastelColors.textRed()) // Spec 057

        statusText.Text <- text
        statusText.SetTextColor(color)


    /// Update the Info pane content.
    member private this.UpdateInfoPane() =
        if isNull infoPaneText then () else
        infoPaneText.Text <- this.BuildInfoPaneText()


    /// Update the Log pane content.
    member private this.UpdateLogPane() =
        if isNull logPaneText || isNull logScrollView then () else
        logPaneText.Text <- LogBuffer.getText()
        // Auto-scroll to bottom if user hasn't scrolled up
        if not userScrolledUp then
            logScrollView.Post(fun () ->
                logScrollView.FullScroll(FocusSearchDirection.Down) |> ignore
            ) |> ignore


    /// Spec 057: Update title bar color based on version state.
    member private this.UpdateTitleBar() =
        if isNull titleBar then () else
        let bgColor =
            match versionState with
            | VersionOkState -> TitleBarColors.normal()
            | VersionWarnState -> TitleBarColors.versionWarn()
            | VersionErrorState -> TitleBarColors.versionError()
        titleBar.SetBackgroundColor(bgColor)


    /// Full UI update.
    member private this.UpdateUI() =
        this.RunOnUiThread(fun () ->
            this.UpdatePowerButton()
            this.UpdateStatusText()
            this.UpdateTitleBar()  // Spec 057
            this.UpdateInfoPane()
            this.UpdateLogPane()
        )


    /// Map service state to UI state (spec 050/057).
    member private this.mapServiceStateToUiState (serviceState: VpnServiceConnectionState) =
        match serviceState with
        | VpnServiceConnectionState.Disconnected -> Disconnected
        | VpnServiceConnectionState.Connecting -> Connecting
        | VpnServiceConnectionState.Connected -> Connected
        | VpnServiceConnectionState.Reconnecting -> Reconnecting
        | VpnServiceConnectionState.Failed _ -> Disconnected
        | VpnServiceConnectionState.VersionError _ -> Disconnected  // Spec 057: Treat as disconnected

    /// Spec 057: Update version state from service.
    member private this.updateVersionState (svc: VpnTunnelServiceImpl) =
        match svc.VersionCheckResult with
        | Some (VersionCheckOk, info) ->
            versionState <- VersionOkState
            versionInfo <- Some info
        | Some (VersionCheckWarn _, info) ->
            versionState <- VersionWarnState
            versionInfo <- Some info
        | Some (VersionCheckError _, info) ->
            versionState <- VersionErrorState
            versionInfo <- Some info
        | None -> ()

    member private this.UpdateStats() =
        match vpnService with
        | Some svc when svc.IsRunning ->
            let (sent, recv, pktSent, pktRecv) = svc.GetStats()
            bytesSent <- sent
            bytesReceived <- recv
            packetsSent <- pktSent
            packetsReceived <- pktRecv
            sessionId <- svc.SessionId
            // Spec 050: Read state and lastError from service
            let newState = this.mapServiceStateToUiState svc.State
            // Spec 055: Clear pending disconnect when state becomes Disconnected
            if newState = Disconnected then pendingDisconnect <- false
            connectionState <- newState
            lastError <- svc.LastError
            // Spec 057: Update version state
            this.updateVersionState svc
            this.UpdateUI()
        | Some svc ->
            // Service exists but not running - still read state
            let newState = this.mapServiceStateToUiState svc.State
            // Spec 055: Clear pending disconnect when state becomes Disconnected
            if newState = Disconnected then pendingDisconnect <- false
            connectionState <- newState
            lastError <- svc.LastError
            // Spec 057: Update version state
            this.updateVersionState svc
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
            clientId <- config.clientId
            serverId <- config.serverId
            match toVpnClientServiceData config with
            | Ok data ->
                serviceData <- Some data
                configLoadError <- None
                Logger.logInfo $"Config loaded: {serverHost}:{basicHttpPort}"
            | Error e ->
                this.SetLastError $"Failed to convert config: {e}"
                serviceData <- None
                configLoadError <- Some e
        | Error e ->
            this.SetLastError $"Failed to load config: {e}"
            serviceData <- None
            configLoadError <- Some e


    member private this.RequestVpnPermission() =
        let intent = VpnService.Prepare(this)
        if intent <> null then
            this.StartActivityForResult(intent, RequestCodes.VpnPermission)
        else
            this.StartVpnConnection()


    member private this.StartVpnConnection() =
        match serviceData with
        | Some data ->
            async {
                try
                    let svc = new VpnTunnelServiceImpl()
                    svc.SetContext(this)
                    vpnService <- Some svc
                    if svc.StartVpn(data) then
                        // Spec 050: Read state from service
                        connectionState <- this.mapServiceStateToUiState svc.State
                        sessionId <- svc.SessionId
                        lastError <- svc.LastError
                        this.StartStatsTimer()
                        Logger.logInfo "VPN connected successfully"
                    else
                        // Spec 050: Read state and lastError from service
                        connectionState <- this.mapServiceStateToUiState svc.State
                        lastError <- svc.LastError
                        vpnService <- None
                with
                | ex ->
                    this.SetLastError $"VPN start failed: {ex.Message}"
                    connectionState <- Disconnected
                    vpnService <- None
                this.UpdateUI()
            } |> Async.Start
        | None ->
            this.SetLastError "No config loaded"
            connectionState <- Disconnected
            this.UpdateUI()


    member private this.StopVpnConnection() =
        this.StopStatsTimer()
        match vpnService with
        | Some svc ->
            svc.StopVpn()
            vpnService <- None
            Logger.logInfo "VPN disconnected"
        | None -> ()
        sessionId <- 0uy
        bytesSent <- 0L
        bytesReceived <- 0L
        packetsSent <- 0L
        packetsReceived <- 0L
        connectionState <- Disconnected
        pendingDisconnect <- false // Spec 055: Clear pending flag when disconnect completes
        this.UpdateUI()


    member private this.OnPowerButtonClick() =
        match connectionState with
        | Disconnected ->
            connectionState <- Connecting
            this.UpdateUI()
            this.RequestVpnPermission()
        | Connecting
        | Connected
        | Reconnecting ->
            // Spec 055: Set pending disconnect immediately for instant UI feedback
            pendingDisconnect <- true
            this.UpdateUI()
            // Spec 055: Run StopVpnConnection asynchronously so UI update happens first
            async { this.StopVpnConnection() } |> Async.Start
        | VersionMismatch ->
            this.UpdateUI()


    member private this.CopyToClipboard(text: string, label: string) =
        let clipboard = this.GetSystemService(Context.ClipboardService) :?> Android.Content.ClipboardManager
        let clip = ClipData.NewPlainText(label, text)
        clipboard.PrimaryClip <- clip
        Toast.MakeText(this, $"{label} copied", ToastLength.Short).Show()


    /// Create the top control row with power button and status text.
    member private this.CreateTopControlRow() =
        let row = new LinearLayout(this)
        row.Orientation <- Orientation.Horizontal
        row.SetGravity(GravityFlags.CenterVertical)
        let rowParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            LinearLayout.LayoutParams.WrapContent)
        rowParams.BottomMargin <- 16
        row.LayoutParameters <- rowParams

        // Power button (round, 52dp) - using ImageButton with vector drawable
        powerButton <- new ImageButton(this)
        let buttonSize = (52.0f * this.Resources.DisplayMetrics.Density) |> int
        let buttonParams = new LinearLayout.LayoutParams(buttonSize, buttonSize)
        buttonParams.RightMargin <- 16
        powerButton.LayoutParameters <- buttonParams
        powerButton.SetImageResource(Resource.Drawable.ic_power)
        powerButton.SetColorFilter(Color.White, PorterDuff.Mode.SrcIn)
        powerButton.SetScaleType(ImageView.ScaleType.CenterInside)
        powerButton.ContentDescription <- "Power"
        powerButton.SetPadding(12, 12, 12, 12)
        powerButton.Click.Add(fun _ -> this.OnPowerButtonClick())
        row.AddView(powerButton)

        // Status text (right of button)
        statusText <- new TextView(this)
        statusText.TextSize <- 20.0f
        statusText.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold)
        let statusParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.WrapContent,
            LinearLayout.LayoutParams.WrapContent)
        statusText.LayoutParameters <- statusParams
        row.AddView(statusText)

        row


    /// Create a pane with title bar containing copy button.
    member private this.CreatePane(title: string, isLogPane: bool) =
        let container = new LinearLayout(this)
        container.Orientation <- Orientation.Vertical
        container.SetBackgroundColor(Color.ParseColor("#F5F5F5"))
        container.SetPadding(8, 8, 8, 8)

        // Title bar with copy button
        let titleBar = new LinearLayout(this)
        titleBar.Orientation <- Orientation.Horizontal
        titleBar.SetGravity(GravityFlags.CenterVertical)
        let titleBarParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            LinearLayout.LayoutParams.WrapContent)
        titleBarParams.BottomMargin <- 4
        titleBar.LayoutParameters <- titleBarParams

        // Title
        let titleView = new TextView(this)
        titleView.Text <- title
        titleView.TextSize <- 14.0f
        titleView.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold)
        let titleParams = new LinearLayout.LayoutParams(
            0, LinearLayout.LayoutParams.WrapContent, 1.0f)
        titleView.LayoutParameters <- titleParams
        titleBar.AddView(titleView)

        // Copy button (icon only)
        let copyButton = new Button(this)
        copyButton.Text <- "📋"
        copyButton.TextSize <- 16.0f
        let copyParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.WrapContent,
            LinearLayout.LayoutParams.WrapContent)
        copyButton.LayoutParameters <- copyParams
        copyButton.SetMinimumWidth(0)
        copyButton.SetMinWidth(0)
        copyButton.SetPadding(16, 4, 16, 4)
        titleBar.AddView(copyButton)

        container.AddView(titleBar)

        // Scroll view for content
        let scrollView = new ScrollView(this)
        let scrollParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            0, 1.0f)
        scrollView.LayoutParameters <- scrollParams

        // Content text
        let textView = new TextView(this)
        textView.TextSize <- 11.0f
        textView.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal)
        textView.SetTextColor(Color.ParseColor("#212121"))
        let textParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            LinearLayout.LayoutParams.WrapContent)
        textView.LayoutParameters <- textParams
        scrollView.AddView(textView)

        container.AddView(scrollView)

        if isLogPane then
            logPaneText <- textView
            logScrollView <- scrollView
            copyButton.Click.Add(fun _ -> this.CopyToClipboard(LogBuffer.getText(), "Log"))

            // Track scroll position to pause auto-scroll
            scrollView.ViewTreeObserver.ScrollChanged.Add(fun _ ->
                let scrollY = scrollView.ScrollY
                let maxScrollY = logPaneText.Height - scrollView.Height
                // If user scrolled up more than 50px from bottom, pause auto-scroll
                userScrolledUp <- scrollY < maxScrollY - 50
            )
        else
            infoPaneText <- textView
            copyButton.Click.Add(fun _ -> this.CopyToClipboard(this.BuildInfoPaneText(), "Info"))

        container


    /// Build the main layout with orientation awareness.
    /// Layout structure (spec 054):
    /// - Band 1: Title bar (bluish, "Softellect VPN") - WrapContent, no weight
    /// - Band 2: Connect button + status row - WrapContent, no weight
    /// - Band 3: Info/Log panes - fills remaining space with weight=1
    member private this.BuildLayout() =
        let mainLayout = new LinearLayout(this)
        mainLayout.Orientation <- Orientation.Vertical
        // Standard padding; system insets are handled by android:fitsSystemWindows in theme
        mainLayout.SetPadding(16, 16, 16, 16)

        // Band 1: Title bar (bluish header with "Softellect VPN")
        // WrapContent height, no weight - must always be visible
        // Spec 057: Color changes based on version state
        let newTitleBar = new LinearLayout(this)
        newTitleBar.Orientation <- Orientation.Horizontal
        newTitleBar.SetGravity(GravityFlags.CenterVertical)
        newTitleBar.SetBackgroundColor(TitleBarColors.normal()) // Primary color from theme
        newTitleBar.SetPadding(16, 12, 16, 12)
        let titleBarParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            LinearLayout.LayoutParams.WrapContent)
        titleBarParams.BottomMargin <- 8
        newTitleBar.LayoutParameters <- titleBarParams
        titleBar <- newTitleBar  // Save reference for color updates

        let titleText = new TextView(this)
        titleText.Text <- "Softellect VPN"
        titleText.TextSize <- 20.0f
        titleText.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold)
        titleText.SetTextColor(Color.White)
        let titleTextParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.WrapContent,
            LinearLayout.LayoutParams.WrapContent)
        titleText.LayoutParameters <- titleTextParams
        newTitleBar.AddView(titleText)

        mainLayout.AddView(newTitleBar)

        // Band 2: Top control row (Connect button + status)
        // WrapContent height, no weight - must always be visible
        let topRow = this.CreateTopControlRow()
        mainLayout.AddView(topRow)

        // Determine orientation for Band 3 panes layout
        let orientation = this.Resources.Configuration.Orientation

        // Band 3: Panes container (Info + Log) - the only flexible region
        // Uses height=0 with weight=1.0 to fill remaining space
        // Vertical in portrait, horizontal in landscape
        let panesContainer = new LinearLayout(this)
        if orientation = Android.Content.Res.Orientation.Landscape then
            panesContainer.Orientation <- Orientation.Horizontal
        else
            panesContainer.Orientation <- Orientation.Vertical

        let panesParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MatchParent,
            0, 1.0f)
        panesContainer.LayoutParameters <- panesParams

        // Info pane
        let infoPane = this.CreatePane("Info", false)
        let infoPaneParams = new LinearLayout.LayoutParams(
            (if orientation = Android.Content.Res.Orientation.Landscape then 0 else LinearLayout.LayoutParams.MatchParent),
            (if orientation = Android.Content.Res.Orientation.Landscape then LinearLayout.LayoutParams.MatchParent else 0),
            1.0f)
        infoPaneParams.SetMargins(0, 0,
            (if orientation = Android.Content.Res.Orientation.Landscape then 8 else 0),
            (if orientation = Android.Content.Res.Orientation.Landscape then 0 else 8))
        infoPane.LayoutParameters <- infoPaneParams
        panesContainer.AddView(infoPane)

        // Log pane
        let logPane = this.CreatePane("Log", true)
        let logPaneParams = new LinearLayout.LayoutParams(
            (if orientation = Android.Content.Res.Orientation.Landscape then 0 else LinearLayout.LayoutParams.MatchParent),
            (if orientation = Android.Content.Res.Orientation.Landscape then LinearLayout.LayoutParams.MatchParent else 0),
            1.0f)
        logPane.LayoutParameters <- logPaneParams
        panesContainer.AddView(logPane)

        mainLayout.AddView(panesContainer)
        mainLayout


    override this.OnActivityResult(requestCode: int, resultCode: Result, data: Intent) =
        base.OnActivityResult(requestCode, resultCode, data)

        match requestCode with
        | RequestCodes.VpnPermission ->
            if resultCode = Result.Ok then
                this.StartVpnConnection()
            else
                this.SetLastError "VPN permission denied"
                connectionState <- Disconnected
                this.UpdateUI()
        | _ -> ()


    override this.OnCreate(savedInstanceState: Bundle) =
        base.OnCreate(savedInstanceState)

        try
            // Configure logging to write to LogBuffer
            configureLogging()

            Logger.logInfo "VPN Android client starting"

            // Build and set the layout FIRST
            let layout = this.BuildLayout()
            this.SetContentView(layout)

            // Set up LogBuffer callback to refresh log pane AFTER UI is built
            LogBuffer.onLogAdded <- Some (fun () ->
                this.RunOnUiThread(fun () -> this.UpdateLogPane())
            )

            // Load config from Assets
            this.LoadConfig()
            this.UpdateUI()
        with
        | ex ->
            // Log to Android logcat in case of early failure
            Android.Util.Log.Error("VpnAndroidClient", $"OnCreate failed: {ex.Message}") |> ignore
            Android.Util.Log.Error("VpnAndroidClient", $"Stack: {ex.StackTrace}") |> ignore


    override this.OnConfigurationChanged(newConfig: Configuration) =
        base.OnConfigurationChanged(newConfig)
        // Rebuild layout on orientation change
        let layout = this.BuildLayout()
        this.SetContentView(layout)
        this.UpdateUI()


    override this.OnDestroy() =
        this.StopStatsTimer()
        match vpnService with
        | Some svc -> svc.StopVpn()
        | None -> ()
        LogBuffer.onLogAdded <- None
        base.OnDestroy()
