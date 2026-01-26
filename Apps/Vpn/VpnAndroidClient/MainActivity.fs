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


/// Spec 065: Service connection for binding to VpnTunnelServiceImpl.
type VpnServiceConnection(onConnected: VpnTunnelServiceImpl -> unit, onDisconnected: unit -> unit) =
    inherit Java.Lang.Object()
    interface Android.Content.IServiceConnection with
        member _.OnServiceConnected(name: ComponentName, binder: IBinder) =
            Logger.logInfo "VpnServiceConnection: Service connected"
            match binder with
            | :? VpnTunnelServiceBinder as vpnBinder ->
                let service = vpnBinder.GetService()
                onConnected service
            | _ ->
                Logger.logError "VpnServiceConnection: Unexpected binder type"

        member _.OnServiceDisconnected(name: ComponentName) =
            Logger.logInfo "VpnServiceConnection: Service disconnected"
            onDisconnected()


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
    let mutable vpnConnectionSpinner: Spinner = null  // Spec 064: VPN connection selector
    let mutable vpnService: VpnTunnelServiceImpl option = None
    let mutable serviceData: VpnClientServiceData option = None
    let mutable statsTimer: Timer = null
    let mutable configLoadError: string option = None
    let mutable lastError: string = ""
    let mutable versionState: VersionState = VersionOkState  // Spec 057: Version state for UI
    let mutable versionInfo: VersionCheckInfo option = None  // Spec 057: Version info for display
    let mutable isOtherVpnBlocking = false  // Spec 065: True if another VPN is blocking our app

    // Spec 065: Service binding fields
    let mutable serviceConnection: VpnServiceConnection option = None
    let mutable isServiceBound = false

    // Spec 064: VPN connection selection
    let mutable vpnConfig: VpnClientConfig option = None
    let mutable parsedConnections: VpnConnectionInfo list = []
    let mutable selectedConnectionName: string = ""
    let mutable reconnectRequired: bool = false

    // Connection info from config
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


    /// Spec 065: Check if our own VPN is connected.
    member private this.isOurVpnConnected() : bool =
        match vpnService with
        | Some svc when svc.IsRunning ->
            match svc.State with
            | VpnServiceConnectionState.Connected -> true
            | _ -> false
        | _ -> false


    /// Spec 065: Check for other VPN blocking and update state with logging.
    /// Only logs on state transitions (not on every call).
    member private this.checkAndUpdateOtherVpnBlocking() : unit =
        let someVpnActive = isSomeVpnActiveOnDevice()
        let ourVpnConnected = this.isOurVpnConnected()
        let nowBlocking = someVpnActive && not ourVpnConnected

        // Get previous state for logging transitions only
        let wasBlocking = getWasOtherVpnBlocking this

        if nowBlocking <> wasBlocking then
            // State transition - log it
            if nowBlocking then
                let infoMsg = "Another VPN is currently active on this device. Disconnect it first, then reopen this app."
                let logMsg = "Detected active VPN transport: blocking actions until the other VPN is disconnected."
                LogBuffer.addLine $"[INFO] {logMsg}"
                Logger.logInfo logMsg
            else
                Logger.logInfo "Other VPN blocking cleared - app functionality restored."

            // Persist new state
            setWasOtherVpnBlocking this nowBlocking

        isOtherVpnBlocking <- nowBlocking


    /// Spec 065: Bind to VPN service.
    member private this.BindVpnService() =
        if not isServiceBound then
            let onConnected (service: VpnTunnelServiceImpl) =
                this.RunOnUiThread(fun () ->
                    vpnService <- Some service
                    isServiceBound <- true
                    Logger.logInfo "VPN service bound successfully"
                    this.UpdateStats()
                )

            let onDisconnected () =
                this.RunOnUiThread(fun () ->
                    Logger.logWarn "VPN service disconnected unexpectedly"
                    isServiceBound <- false
                )

            let conn = new VpnServiceConnection(onConnected, onDisconnected)
            serviceConnection <- Some conn
            let intent = new Intent(this, typeof<VpnTunnelServiceImpl>)
            let bindFlags = Bind.AutoCreate
            let bound = this.BindService(intent, conn, bindFlags)
            if not bound then
                Logger.logError "Failed to bind to VPN service"

    /// Spec 065: Unbind from VPN service.
    member private this.UnbindVpnService() =
        match serviceConnection with
        | Some conn when isServiceBound ->
            try
                this.UnbindService(conn)
                isServiceBound <- false
                serviceConnection <- None
                Logger.logInfo "VPN service unbound"
            with
            | ex ->
                Logger.logError $"Failed to unbind service: {ex.Message}"
        | _ -> ()


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

        // Spec 065: Show blocking message at top if other VPN is active
        if isOtherVpnBlocking then
            sb.AppendLine("⚠ BLOCKED BY OTHER VPN ⚠") |> ignore
            sb.AppendLine("Another VPN is currently active on this device.") |> ignore
            sb.AppendLine("Disconnect it first, then reopen this app.") |> ignore
            sb.AppendLine() |> ignore

        // Configuration section
        sb.AppendLine("── Configuration ──") |> ignore
        sb.AppendLine($"VPN Connection: {selectedConnectionName}") |> ignore
        sb.AppendLine($"ClientId: {clientId}") |> ignore
        sb.AppendLine($"ServerId: {serverId}") |> ignore
        sb.AppendLine($"Reconnect required: {reconnectRequired}") |> ignore
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
        sb.AppendLine() |> ignore

        // Spec 065: Battery optimization hint (one-time, non-blocking)
        if not (hasBatteryHintBeenShown this) then
            sb.AppendLine("── Battery Optimization ──") |> ignore
            sb.AppendLine("If you experience disconnects after long idle,") |> ignore
            sb.AppendLine("exclude Softellect VPN from battery") |> ignore
            sb.AppendLine("optimizations in Android settings.") |> ignore
            // Mark as shown
            markBatteryHintShown this

        sb.ToString()


    /// Update the power button appearance based on state.
    member private this.UpdatePowerButton() =
        if isNull powerButton then () else
        let (bgColor, enabled) =
            // Spec 065: Disable button if other VPN is blocking
            if isOtherVpnBlocking then
                (PastelColors.paleRed(), false)
            else
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
            vpnConfig <- Some config
            clientId <- config.clientId
            serverId <- config.serverId

            // Parse VPN connections
            parsedConnections <- parseVpnConnections config.vpnConnections
            Logger.logInfo $"Found {parsedConnections.Length} VPN connection points."

            if parsedConnections.IsEmpty then
                let errMsg = "No VPN connections configured"
                this.SetLastError errMsg
                serviceData <- None
                configLoadError <- Some errMsg
            else
                // Resolve effective connection name
                match resolveEffectiveVpnConnectionName this config parsedConnections with
                | Some name ->
                    selectedConnectionName <- name
                    match toVpnClientServiceData config name parsedConnections with
                    | Ok data ->
                        serviceData <- Some data
                        configLoadError <- None
                        Logger.logInfo $"Config loaded: VPN connection '{name}'"
                    | Error e ->
                        this.SetLastError $"Failed to convert config: {e}"
                        serviceData <- None
                        configLoadError <- Some e
                | None ->
                    let errMsg = "Failed to resolve VPN connection name"
                    this.SetLastError errMsg
                    serviceData <- None
                    configLoadError <- Some errMsg
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
                    // Spec 065: Start foreground service
                    let intent = new Intent(this, typeof<VpnTunnelServiceImpl>)
                    if Build.VERSION.SdkInt >= BuildVersionCodes.O then
                        this.StartForegroundService(intent) |> ignore
                    else
                        this.StartService(intent) |> ignore

                    Logger.logInfo "VPN service start requested"

                    // Bind to service to get reference
                    this.BindVpnService()

                    // Wait a bit for service to bind, then start VPN
                    do! Async.Sleep(500)

                    match vpnService with
                    | Some svc ->
                        svc.SetContext(this)
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
                    | None ->
                        this.SetLastError "Failed to bind to VPN service"
                        connectionState <- Disconnected
                with
                | ex ->
                    this.SetLastError $"VPN start failed: {ex.Message}"
                    connectionState <- Disconnected
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
            Logger.logInfo "VPN disconnected"
        | None -> ()

        // Spec 065: Stop the service
        let intent = new Intent(this, typeof<VpnTunnelServiceImpl>)
        this.StopService(intent) |> ignore

        sessionId <- 0uy
        bytesSent <- 0L
        bytesReceived <- 0L
        packetsSent <- 0L
        packetsReceived <- 0L
        connectionState <- Disconnected
        pendingDisconnect <- false // Spec 055: Clear pending flag when disconnect completes
        reconnectRequired <- false // Spec 064: Clear reconnect required on disconnect
        this.UpdateUI()


    member private this.OnPowerButtonClick() =
        // Spec 065: No-op with message if other VPN is blocking
        if isOtherVpnBlocking then
            Logger.logWarn "Connect blocked: Another VPN is active on this device."
            this.UpdateUI()
        else
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


    /// Handle VPN connection selection change from Spinner.
    member private this.OnVpnConnectionSelected(newConnectionName: string) =
        // Ignore placeholder "No connections" and same-selection events
        if newConnectionName <> selectedConnectionName
           && not (String.IsNullOrEmpty newConnectionName)
           && newConnectionName <> "No connections"
           && parsedConnections |> List.exists (fun c -> c.vpnConnectionName.value = newConnectionName) then

            let oldName = selectedConnectionName
            selectedConnectionName <- newConnectionName

            // Persist the selection
            persistVpnConnectionName this newConnectionName
            Logger.logInfo $"VPN connection changed from '{oldName}' to '{newConnectionName}'"

            // Mark reconnect required if not disconnected
            if connectionState <> Disconnected then
                reconnectRequired <- true

            // Update service data with new connection
            match vpnConfig with
            | Some config ->
                match toVpnClientServiceData config newConnectionName parsedConnections with
                | Ok data ->
                    serviceData <- Some data
                | Error e ->
                    this.SetLastError $"Failed to update service data: {e}"
            | None -> ()

            this.UpdateUI()


    /// Create the top control row with power button, status text, and VPN connection spinner.
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

        // Status text (right of button) - use weight=1 to push spinner to the right
        statusText <- new TextView(this)
        statusText.TextSize <- 20.0f
        statusText.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold)
        let statusParams = new LinearLayout.LayoutParams(
            0,
            LinearLayout.LayoutParams.WrapContent,
            1.0f)
        statusParams.RightMargin <- 16
        statusText.LayoutParameters <- statusParams
        row.AddView(statusText)

        // VPN Connection Spinner (right-aligned)
        vpnConnectionSpinner <- new Spinner(this)
        let spinnerParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.WrapContent,
            LinearLayout.LayoutParams.WrapContent)
        spinnerParams.Gravity <- GravityFlags.End ||| GravityFlags.CenterVertical
        vpnConnectionSpinner.LayoutParameters <- spinnerParams

        // Populate spinner with VPN connection names
        let connectionNames =
            if parsedConnections.IsEmpty then [| "No connections" |]
            else parsedConnections |> List.map (fun c -> c.vpnConnectionName.value) |> List.toArray

        let adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, connectionNames)
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem)
        vpnConnectionSpinner.Adapter <- adapter

        // Set initial selection
        if not parsedConnections.IsEmpty then
            let selectedIndex = connectionNames |> Array.tryFindIndex (fun n -> n = selectedConnectionName) |> Option.defaultValue 0
            vpnConnectionSpinner.SetSelection(selectedIndex)

        // Handle selection changes
        vpnConnectionSpinner.ItemSelected.Add(fun args ->
            let selected = connectionNames.[args.Position]
            this.OnVpnConnectionSelected(selected)
        )

        // Disable spinner if no valid connections
        vpnConnectionSpinner.Enabled <- not parsedConnections.IsEmpty

        row.AddView(vpnConnectionSpinner)

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

            // Spec 065: Check for other VPN BEFORE loading config (early gate)
            this.checkAndUpdateOtherVpnBlocking()

            // If blocked by other VPN, skip config loading and network queries
            if not isOtherVpnBlocking then
                // Load config from Assets FIRST so parsedConnections is populated before UI build
                this.LoadConfig()

            // Build and set the layout (whether blocked or not, we need UI)
            let layout = this.BuildLayout()
            this.SetContentView(layout)

            // Set up LogBuffer callback to refresh log pane AFTER UI is built
            LogBuffer.onLogAdded <- Some (fun () ->
                this.RunOnUiThread(fun () -> this.UpdateLogPane())
            )

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


    override this.OnResume() =
        base.OnResume()
        // Spec 065: Check for other VPN blocking on resume
        this.checkAndUpdateOtherVpnBlocking()
        // Spec 065: Bind to service if not already bound (to query state)
        if not isServiceBound then
            this.BindVpnService()
        this.UpdateUI()


    override this.OnDestroy() =
        this.StopStatsTimer()
        // Spec 065: Unbind service (but don't stop it - let it run in background)
        this.UnbindVpnService()
        vpnService <- None
        LogBuffer.onLogAdded <- None
        base.OnDestroy()
