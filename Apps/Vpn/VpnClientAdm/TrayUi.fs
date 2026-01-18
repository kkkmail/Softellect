namespace Softellect.Vpn.ClientAdm

open System
open System.Drawing
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Windows.Forms
open Softellect.Sys.Logging
open Softellect.Vpn.ClientAdm.CommandLine
open Softellect.Vpn.ClientAdm.Implementation
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Client.AdminService

module TrayUi =

    [<DllImport("kernel32.dll")>]
    extern nativeint GetConsoleWindow()


    [<DllImport("user32.dll")>]
    extern bool ShowWindow(nativeint hWnd, int nCmdShow)


    [<DllImport("user32.dll", CharSet = CharSet.Auto)>]
    extern uint32 RegisterWindowMessage(string msg)


    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool FreeConsole()


    let private SW_HIDE = 0
    let private SW_SHOW = 5


    let hideConsoleWindow () =
        let hWnd = GetConsoleWindow()
        if hWnd <> nativeint 0 then
            ShowWindow(hWnd, SW_HIDE) |> ignore


    let showConsoleWindow () =
        let hWnd = GetConsoleWindow()
        if hWnd <> nativeint 0 then
            ShowWindow(hWnd, SW_SHOW) |> ignore


    let detachConsole () =
        // Detach from the parent console (FAR, cmd.exe, etc.)
        if FreeConsole() then
            // Redirect standard streams to NUL to avoid accidental writes
            let nullWriter = new StreamWriter(Stream.Null)
            nullWriter.AutoFlush <- true
            Console.SetOut(nullWriter)
            Console.SetError(nullWriter)
            Console.SetIn(new StreamReader(Stream.Null))


    /// UI-only state when the service is unreachable or querying.
    type TrayUiState =
        | ServiceState of VpnClientConnectionState
        | ServiceNotRunning
        | QueryingStatus


    /// Pastel colors matching Android client (spec 058).
    module StateColors =
        let disconnected = Color.FromArgb(229, 115, 115)  // #E57373 - pale red
        let connecting = Color.FromArgb(255, 213, 79)     // #FFD54F - pale yellow
        let connected = Color.FromArgb(129, 199, 132)     // #81C784 - pale green
        let reconnecting = Color.FromArgb(255, 183, 77)   // #FFB74D - pale orange
        let failed = Color.FromArgb(229, 115, 115)        // #E57373 - pale red
        let serviceNotRunning = Color.FromArgb(158, 158, 158) // gray


    /// Get display text for a state.
    let getStateText (state: TrayUiState) =
        match state with
        | ServiceState s ->
            match s with
            | Disconnected -> "Disconnected"
            | Connecting -> "Connecting..."
            | Connected ip -> $"Connected: {ip.value.value}"
            | Reconnecting -> "Reconnecting..."
            | Failed msg -> $"Failed: {msg}"
            | VersionError msg -> $"Version Error: {msg}"
        | ServiceNotRunning -> "Service Not Running"
        | QueryingStatus -> "Checking service..."


    /// Get color for a state.
    let getStateColor (state: TrayUiState) =
        match state with
        | ServiceState s ->
            match s with
            | Disconnected -> StateColors.disconnected
            | Connecting -> StateColors.connecting
            | Connected _ -> StateColors.connected
            | Reconnecting -> StateColors.reconnecting
            | Failed _ -> StateColors.failed
            | VersionError _ -> StateColors.failed
        | ServiceNotRunning -> StateColors.serviceNotRunning
        | QueryingStatus -> StateColors.serviceNotRunning


    /// Check if the current state is "connected" (for toggle logic).
    let isConnected (state: TrayUiState) =
        match state with
        | ServiceState (Connected _) -> true
        | _ -> false


    /// Create a simple icon with the given color.
    let createIcon (color: Color) =
        let size = 16
        let bitmap = new Bitmap(size, size)
        use g = Graphics.FromImage(bitmap)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use brush = new SolidBrush(color)
        g.FillEllipse(brush, 2, 2, size - 4, size - 4)
        Icon.FromHandle(bitmap.GetHicon())


    /// Windows message for taskbar recreation (Explorer restart).
    let WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated")


    /// Tray application context with single instance support.
    type TrayApplicationContext() as this =
        inherit ApplicationContext()

        let adminAccessInfo = loadAdminAccessInfo()
        let adminClient = createAdminWcfClient adminAccessInfo

        // Load VPN connections and selected connection name
        let vpnConnections = loadVpnConnections()
        let mutable selectedVpnConnectionName =
            match loadSelectedVpnConnectionName() with
            | Some name ->
                // Validate against available connections
                if vpnConnections |> List.exists (fun c -> c.vpnConnectionName = name) then name
                else
                    // Invalid name - use first available or default
                    match vpnConnections with
                    | h :: _ ->
                        Logger.logWarn $"TrayUi: Configured VPN name '{name.value}' not found in available connections. Using '{h.vpnConnectionName.value}'."
                        h.vpnConnectionName
                    | [] -> VpnConnectionName.defaultValue
            | None ->
                // No configured name - use first available or default
                match vpnConnections with
                | h :: _ -> h.vpnConnectionName
                | [] -> VpnConnectionName.defaultValue

        let mutable autoStart = loadAutoStart()
        let mutable currentState = ServiceNotRunning
        let mutable reconnectRequired = false
        let mutable notifyIcon : NotifyIcon option = None
        let mutable contextMenu : ContextMenuStrip option = None
        let mutable vpnConnectionMenuItems : ToolStripMenuItem list = []
        let mutable autoStartMenuItem : ToolStripMenuItem option = None
        let mutable isDisposed = false
        let mutable isQueryingStatus = false
        let mutable marshalControl : Control option = None

        /// Get the tooltip text based on current state and VPN connection name.
        let getTooltipText () =
            if vpnConnections.IsEmpty then
                "No VPN connections configured"
            else
                let baseText =
                    match currentState with
                    | ServiceState (Connected ip) ->
                        $"{selectedVpnConnectionName.value}: {ip.value.value}"
                    | QueryingStatus ->
                        "Checking service..."
                    | _ ->
                        getStateText currentState

                if reconnectRequired then
                    $"{baseText} (reconnect required)"
                else
                    baseText

        /// Update the tray icon and tooltip based on the current state.
        let updateUi () =
            match notifyIcon with
            | Some icon ->
                let color =
                    if vpnConnections.IsEmpty then StateColors.serviceNotRunning
                    else getStateColor currentState
                let text = getTooltipText()

                // Dispose old icon before creating a new one
                if icon.Icon <> null then
                    icon.Icon.Dispose()

                icon.Icon <- createIcon color
                // Tooltip is limited to 63 characters
                icon.Text <- if text.Length > 63 then text.Substring(0, 60) + "..." else text
            | None -> ()

        /// Query status from the service asynchronously.
        let queryStatusAsync () =
            if not isQueryingStatus then
                isQueryingStatus <- true
                currentState <- QueryingStatus
                updateUi()

                // Start background thread for the query
                let worker = new System.ComponentModel.BackgroundWorker()
                worker.DoWork.Add(fun e ->
                    try
                        match adminClient.getStatus() with
                        | Ok state -> e.Result <- box (Some state)
                        | Error err ->
                            Logger.logWarn $"TrayUi: Failed to get status: '%A{err}'"
                            e.Result <- box None
                    with
                    | ex ->
                        Logger.logWarn $"TrayUi: Exception getting status: {ex.Message}"
                        e.Result <- box None
                )

                worker.RunWorkerCompleted.Add(fun e ->
                    // Marshal back to UI thread
                    match marshalControl with
                    | Some ctrl when not ctrl.IsDisposed ->
                        ctrl.BeginInvoke(Action(fun () ->
                            match e.Result :?> VpnClientConnectionState option with
                            | Some state ->
                                // Clear reconnect required flag when disconnected
                                if not (isConnected (ServiceState state)) then
                                    reconnectRequired <- false
                                currentState <- ServiceState state
                            | None ->
                                currentState <- ServiceNotRunning

                            isQueryingStatus <- false
                            updateUi()
                            worker.Dispose()
                        )) |> ignore
                    | _ ->
                        isQueryingStatus <- false
                        worker.Dispose()
                )

                worker.RunWorkerAsync()

        /// Handle click - toggle VPN connection.
        let handleClick () =
            try
                if isConnected currentState then
                    Logger.logInfo "TrayUi: User clicked - stopping VPN..."
                    match adminClient.stopVpn() with
                    | Ok () -> Logger.logInfo "TrayUi: Stop command sent successfully."
                    | Error e -> Logger.logError $"TrayUi: Stop command failed: '%A{e}'"
                else
                    Logger.logInfo "TrayUi: User clicked - starting VPN..."
                    match adminClient.startVpn() with
                    | Ok () -> Logger.logInfo "TrayUi: Start command sent successfully."
                    | Error e -> Logger.logError $"TrayUi: Start command failed: '%A{e}'"

                // Query status after the command completes
                queryStatusAsync()
            with
            | ex ->
                Logger.logError $"TrayUi: Exception during click handling: {ex.Message}"
                currentState <- ServiceNotRunning
                updateUi()

        /// Handle hover - refresh status.
        let handleMouseMove () =
            queryStatusAsync()

        /// Handle Exit menu item.
        let handleExit () =
            Logger.logInfo "TrayUi: User selected Exit."
            this.ExitThread()

        /// Handle VPN connection selection.
        let handleVpnConnectionSelect (connectionName: VpnConnectionName) =
            if connectionName <> selectedVpnConnectionName then
                let oldName = selectedVpnConnectionName.value
                selectedVpnConnectionName <- connectionName

                // Persist the selection
                match saveSelectedVpnConnectionName connectionName with
                | Ok () ->
                    Logger.logInfo $"TrayUi: VPN connection changed from '{oldName}' to '{connectionName.value}'."
                | Error e ->
                    Logger.logError $"TrayUi: Failed to save VPN connection selection: '%A{e}'."

                // Update checked state for all menu items
                vpnConnectionMenuItems |> List.iter (fun item ->
                    item.Checked <- (item.Tag :?> VpnConnectionName) = connectionName
                )

                // Set reconnect required if currently connected
                if isConnected currentState then
                    reconnectRequired <- true

                updateUi()

        /// Handle Auto Start toggle.
        let handleAutoStartToggle () =
            autoStart <- not autoStart

            // Persist the setting
            match saveAutoStart autoStart with
            | Ok () ->
                Logger.logInfo $"TrayUi: Auto Start set to {autoStart}."
            | Error e ->
                Logger.logError $"TrayUi: Failed to save Auto Start setting: '%A{e}'."

            // Update menu item checked state
            match autoStartMenuItem with
            | Some item -> item.Checked <- autoStart
            | None -> ()

        /// Create the context menu.
        let createContextMenu () =
            let menu = new ContextMenuStrip()

            // VPN Connection submenu
            let vpnConnectionMenu = new ToolStripMenuItem("VPN Connection")

            if vpnConnections.IsEmpty then
                // No connections available - show disabled placeholder
                let noConnectionsItem = new ToolStripMenuItem("(No VPN connections available)")
                noConnectionsItem.Enabled <- false
                vpnConnectionMenu.DropDownItems.Add(noConnectionsItem) |> ignore
            else
                // Populate with available VPN connections
                vpnConnectionMenuItems <- vpnConnections |> List.map (fun conn ->
                    let item = new ToolStripMenuItem(conn.vpnConnectionName.value)
                    item.Tag <- conn.vpnConnectionName
                    item.CheckOnClick <- false
                    item.Checked <- conn.vpnConnectionName = selectedVpnConnectionName
                    item.Click.Add(fun _ -> handleVpnConnectionSelect conn.vpnConnectionName)
                    vpnConnectionMenu.DropDownItems.Add(item) |> ignore
                    item
                )

            menu.Items.Add(vpnConnectionMenu) |> ignore

            // Auto Start checkbox
            let autoStartItem = new ToolStripMenuItem("Auto Start")
            autoStartItem.CheckOnClick <- false
            autoStartItem.Checked <- autoStart
            autoStartItem.Click.Add(fun _ -> handleAutoStartToggle())
            autoStartMenuItem <- Some autoStartItem
            menu.Items.Add(autoStartItem) |> ignore

            // Exit item
            let exitItem = new ToolStripMenuItem("Exit")
            exitItem.Click.Add(fun _ -> handleExit())
            menu.Items.Add(exitItem) |> ignore

            menu

        /// Create and set up the notification icon.
        let createNotifyIcon () =
            let icon = new NotifyIcon()
            icon.Visible <- true

            // Initial state - use grey if no connections
            let initialColor =
                if vpnConnections.IsEmpty then StateColors.serviceNotRunning
                else StateColors.serviceNotRunning
            icon.Icon <- createIcon initialColor
            icon.Text <- "VPN Client - Initializing..."

            // Setup event handlers
            icon.Click.Add(fun e ->
                let me = e :?> MouseEventArgs
                if me.Button = MouseButtons.Left then
                    handleClick())

            icon.MouseMove.Add(fun _ -> handleMouseMove())

            // Setup context menu
            let menu = createContextMenu()
            contextMenu <- Some menu
            icon.ContextMenuStrip <- menu

            icon

        /// Initialize the tray UI.
        let initialize () =
            Logger.logInfo "TrayUi: Initializing..."
            Logger.logInfo $"TrayUi: Admin access info: '{adminAccessInfo}'"
            Logger.logInfo $"TrayUi: Found {vpnConnections.Length} VPN connections."
            Logger.logInfo $"TrayUi: Selected VPN connection: '{selectedVpnConnectionName.value}'."
            Logger.logInfo $"TrayUi: Auto Start: {autoStart}."

            let icon = createNotifyIcon()
            notifyIcon <- Some icon

            Logger.logInfo "TrayUi: Initialized successfully."

        do initialize()

        /// Set the marshal control for UI thread invocation.
        member _.SetMarshalControl(ctrl: Control) =
            marshalControl <- Some ctrl
            // Now that we have a marshal control, query initial status
            queryStatusAsync()

        /// Handle Windows messages (for Explorer restart detection).
        member _.ProcessMessage(m: Message) =
            if uint32 m.Msg = WM_TASKBARCREATED then
                Logger.logInfo "TrayUi: Taskbar recreated (Explorer restart detected). Re-creating tray icon..."
                // Re-show the icon after Explorer restart
                match notifyIcon with
                | Some icon ->
                    icon.Visible <- false
                    icon.Visible <- true
                | None -> ()

        override _.Dispose(disposing: bool) =
            if not isDisposed then
                if disposing then
                    match notifyIcon with
                    | Some icon ->
                        icon.Visible <- false
                        if icon.Icon <> null then
                            icon.Icon.Dispose()
                        icon.Dispose()
                    | None -> ()

                    match contextMenu with
                    | Some menu -> menu.Dispose()
                    | None -> ()

                isDisposed <- true

            base.Dispose(disposing)


    /// Hidden form for receiving Windows messages (Explorer restart).
    type MessageWindow(context: TrayApplicationContext) as this =
        inherit Form()

        do
            this.ShowInTaskbar <- false
            this.WindowState <- FormWindowState.Minimized
            this.Visible <- false
            this.FormBorderStyle <- FormBorderStyle.None
            this.Size <- Size(0, 0)
            // Register this form as the marshal control for UI thread invocation
            context.SetMarshalControl(this)

        override _.WndProc(m: Message byref) =
            context.ProcessMessage(m)
            base.WndProc(&m)


    /// Mutex name for a single instance check.
    let mutexName = "Global\\Softellect.Vpn.ClientAdm.TrayUi"


    /// Run the tray UI application.
    /// Returns Ok if started successfully, Error if another instance is already running.
    let run (_ctx: ClientAdmContext) (args: TrayArgs list) =
        let showConsole = args |> List.tryPick (function ShowConsole s -> Some s) |> Option.defaultValue false
        if not showConsole then detachConsole()

        // Single instance check
        let mutable createdNew = false
        use mutex = new Mutex(true, mutexName, &createdNew)

        if not createdNew then
            let msg = "TrayUi: Another instance is already running. Exiting."
            Logger.logWarn msg
            Error msg
        else
            try
                Logger.logInfo "TrayUi: Starting tray UI application..."
                Application.EnableVisualStyles()
                Application.SetCompatibleTextRenderingDefault(false)

                use context = new TrayApplicationContext()
                use messageWindow = new MessageWindow(context)

                Application.Run(context)

                let msg = "TrayUi: Tray UI application exited."
                Logger.logInfo msg
                Ok msg
            with
            | ex ->
                let msg = $"TrayUi: Fatal error: {ex.Message}"
                Logger.logCrit msg
                Error msg
