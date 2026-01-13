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


    /// UI-only state when the service is unreachable.
    type TrayUiState =
        | ServiceState of VpnClientConnectionState
        | ServiceNotRunning


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

        let mutable currentState = ServiceNotRunning
        let mutable notifyIcon : NotifyIcon option = None
        let mutable contextMenu : ContextMenuStrip option = None
        let mutable isDisposed = false

        /// Update the tray icon and tooltip based on the current state.
        let updateUi () =
            match notifyIcon with
            | Some icon ->
                let color = getStateColor currentState
                let text = getStateText currentState

                // Dispose old icon before creating a new one
                if icon.Icon <> null then
                    icon.Icon.Dispose()

                icon.Icon <- createIcon color
                // Tooltip is limited to 63 characters
                icon.Text <- if text.Length > 63 then text.Substring(0, 60) + "..." else text
            | None -> ()

        /// Query status from the service.
        let queryStatus () =
            try
                match adminClient.getStatus() with
                | Ok state ->
                    currentState <- ServiceState state
                | Error e ->
                    Logger.logWarn $"TrayUi: Failed to get status: '%A{e}'"
                    currentState <- ServiceNotRunning
            with
            | ex ->
                Logger.logWarn $"TrayUi: Exception getting status: {ex.Message}"
                currentState <- ServiceNotRunning

            updateUi()

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
                queryStatus()
            with
            | ex ->
                Logger.logError $"TrayUi: Exception during click handling: {ex.Message}"
                currentState <- ServiceNotRunning
                updateUi()

        /// Handle hover - refresh status.
        let handleMouseMove () =
            queryStatus()

        /// Handle Exit menu item.
        let handleExit () =
            Logger.logInfo "TrayUi: User selected Exit."
            this.ExitThread()

        /// Create the context menu.
        let createContextMenu () =
            let menu = new ContextMenuStrip()
            let exitItem = new ToolStripMenuItem("Exit")
            exitItem.Click.Add(fun _ -> handleExit())
            menu.Items.Add(exitItem) |> ignore
            menu

        /// Create and set up the notification icon.
        let createNotifyIcon () =
            let icon = new NotifyIcon()
            icon.Visible <- true

            // Initial state
            icon.Icon <- createIcon StateColors.serviceNotRunning
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

            let icon = createNotifyIcon()
            notifyIcon <- Some icon

            // Query initial status
            queryStatus()

            Logger.logInfo "TrayUi: Initialized successfully."

        do initialize()

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
