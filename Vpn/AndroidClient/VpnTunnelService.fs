namespace Softellect.Vpn.AndroidClient

open System
open System.Threading
open Android.App
open Android.Content
open Android.Net
open Android.OS
open Java.IO
open Softellect.Sys.Logging
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Client.UdpClient
open Softellect.Vpn.Client.WcfClient
open Softellect.Vpn.AndroidClient.ConfigManager

module VpnTunnelService =

    /// MTU for VPN tunnel (hardcoded per spec 047).
    [<Literal>]
    let TunnelMtu = 1300

    /// VPN tunnel read buffer size.
    [<Literal>]
    let TunnelBufferSize = 2048

    /// Health check interval in ms (same as Windows: 30 seconds between successful checks).
    [<Literal>]
    let HealthCheckIntervalMs = 30000

    /// Maximum health check backoff in ms (same as Windows: cap at 5 minutes).
    [<Literal>]
    let MaxHealthCheckBackoffMs = 300000

    /// Backoff delay when auth is not available (same as Windows: 100ms).
    [<Literal>]
    let NoAuthBackoffMs = 100


/// VPN connection state for service layer (spec 050).
type VpnServiceConnectionState =
    | Disconnected
    | Connecting
    | Connected
    | Reconnecting
    | Failed of string


/// Android VpnService implementation for Softellect VPN.
/// Note: This class is used directly (not as a bound service).
/// Call SetContext() before StartVpn() to provide the Android context.
/// Implements resilience/reconnect per spec 050.
[<Service(Name = "com.softellect.vpn.VpnTunnelService", Permission = "android.permission.BIND_VPN_SERVICE")>]
type VpnTunnelServiceImpl() =
    inherit VpnService()

    let mutable context: Context = null
    let mutable vpnInterface: ParcelFileDescriptor = null
    let mutable tunInputStream: FileInputStream = null
    let mutable tunOutputStream: FileOutputStream = null
    let mutable udpClient: VpnPushUdpClient option = None
    let mutable authClient: IAuthClient option = None
    let mutable authResponse: VpnAuthResponse option = None
    let mutable cts: CancellationTokenSource = null
    let mutable tunReadThread: Thread = null
    let mutable tunWriteThread: Thread = null
    let mutable supervisorThread: Thread = null

    let mutable bytesSent = 0L
    let mutable bytesReceived = 0L
    let mutable packetsSent = 0L
    let mutable packetsReceived = 0L

    // Spec 050: Connection state and last error tracking
    let mutable connectionState = VpnServiceConnectionState.Disconnected
    let mutable lastError: string = ""
    let stateLock = obj()

    // Spec 056: Device binding hash (computed once at StartVpn from context)
    let mutable clientHash: VpnClientHash option = None

    let authLock = obj()

    /// Get current auth response (thread-safe).
    let getAuth () =
        lock authLock (fun () -> authResponse)

    /// Set current auth response (thread-safe).
    let setAuth auth =
        lock authLock (fun () -> authResponse <- auth)

    /// Get current connection state (thread-safe).
    let getState () =
        lock stateLock (fun () -> connectionState)

    /// Set current connection state (thread-safe).
    let setState state =
        lock stateLock (fun () -> connectionState <- state)

    /// Get last error (thread-safe).
    let getLastError () =
        lock stateLock (fun () -> lastError)

    /// Set last error (thread-safe).
    let setLastError err =
        lock stateLock (fun () ->
            lastError <- err
            if not (String.IsNullOrEmpty err) then
                Logger.logWarn $"VPN service error: {err}"
        )

    /// Clear last error (thread-safe).
    let clearLastError () =
        lock stateLock (fun () -> lastError <- "")

    /// Packet injector that writes to TUN.
    let createPacketInjector (outputStream: FileOutputStream) =
        { new IPacketInjector with
            member _.injectPacket(packet: byte[]) =
                try
                    outputStream.Write(packet, 0, packet.Length)
                    Interlocked.Increment(&packetsReceived) |> ignore
                    Interlocked.Add(&bytesReceived, int64 packet.Length) |> ignore
                    Ok ()
                with
                | ex -> Error ex.Message
        }

    /// TUN read loop - reads from TUN and sends to UDP.
    let tunReadLoop (inputStream: FileInputStream) (udp: VpnPushUdpClient) (token: CancellationToken) =
        let buffer = Array.zeroCreate<byte> VpnTunnelService.TunnelBufferSize
        Logger.logInfo "TUN read loop started"
        let mutable shouldExit = false

        while not token.IsCancellationRequested && not shouldExit do
            try
                let bytesRead = inputStream.Read(buffer, 0, buffer.Length)
                if bytesRead > 0 then
                    let packet = Array.sub buffer 0 bytesRead
                    if udp.enqueueOutbound(packet) then
                        Interlocked.Increment(&packetsSent) |> ignore
                        Interlocked.Add(&bytesSent, int64 bytesRead) |> ignore
                elif bytesRead < 0 then
                    // EOF or error - exit loop
                    Logger.logInfo "TUN read returned EOF, exiting loop"
                    shouldExit <- true
            with
            | :? ObjectDisposedException ->
                shouldExit <- true
            | ex when ex.Message.Contains("EBADF") ->
                // Bad file descriptor - TUN interface was closed
                Logger.logInfo "TUN file descriptor closed, exiting read loop"
                shouldExit <- true
            | ex when not token.IsCancellationRequested ->
                Logger.logError $"TUN read error: {ex.Message}"
                shouldExit <- true // Exit on any unrecoverable error

        Logger.logInfo "TUN read loop stopped"

    /// TUN write loop - reads from UDP inbound queue and writes to TUN.
    let tunWriteLoop (outputStream: FileOutputStream) (udp: VpnPushUdpClient) (token: CancellationToken) =
        Logger.logInfo "TUN write loop started"
        let mutable shouldExit = false

        while not token.IsCancellationRequested && not shouldExit do
            try
                // Wait for packets with timeout
                if udp.inboundQueue.wait(100) then
                    match udp.tryDequeueInbound() with
                    | Some packet ->
                        outputStream.Write(packet, 0, packet.Length)
                        Interlocked.Increment(&packetsReceived) |> ignore
                        Interlocked.Add(&bytesReceived, int64 packet.Length) |> ignore
                    | None -> ()
            with
            | :? ObjectDisposedException ->
                shouldExit <- true
            | ex when ex.Message.Contains("EBADF") ->
                Logger.logInfo "TUN file descriptor closed, exiting write loop"
                shouldExit <- true
            | ex when not token.IsCancellationRequested ->
                Logger.logError $"TUN write error: {ex.Message}"
                shouldExit <- true

        Logger.logInfo "TUN write loop stopped"

    /// Ping the server to check if the session is still valid.
    let pingSession (client: IAuthClient) (clientId: VpnClientId) (auth: VpnAuthResponse) =
        let pingRequest =
            {
                clientId = clientId
                sessionId = auth.sessionId
                timestamp = DateTime.UtcNow
            }
        try
            match client.pingSession pingRequest with
            | Ok () -> true
            | Error e ->
                setLastError $"Ping failed: %A{e}"
                false
        with
        | ex ->
            setLastError $"Ping exception: {ex.Message}"
            false

    /// Authenticate with the server.
    /// Spec 056: clientHash is now required for device binding.
    let authenticate (client: IAuthClient) (clientId: VpnClientId) (clientHash: VpnClientHash) =
        Logger.logInfo "Authenticating with server..."
        let authRequest =
            {
                clientId = clientId
                clientHash = clientHash
                timestamp = DateTime.UtcNow
                nonce = Guid.NewGuid().ToByteArray()
            }
        try
            match client.authenticate authRequest with
            | Ok response ->
                Logger.logInfo $"Authentication successful, sessionId: {response.sessionId.value}"
                clearLastError ()
                Ok response
            | Error e ->
                let errMsg = $"Authentication failed: %A{e}"
                setLastError errMsg
                Error errMsg
        with
        | ex ->
            let errMsg = $"Authentication exception: {ex.Message}"
            setLastError errMsg
            Error errMsg

    let closeVpnInterface () =
        // Close VPN interface
        if vpnInterface <> null then
            try vpnInterface.Close() with | _ -> ()
            vpnInterface <- null

    /// Supervisor loop - handles health checks and reconnection (spec 050).
    /// Runs forever until stopped. Mirrors Windows logic with Android-specific network refresh.
    /// Spec 056: hash parameter for device binding.
    let supervisorLoop (serviceData: VpnClientServiceData) (hash: VpnClientHash) (token: CancellationToken) =
        Logger.logInfo "Supervisor loop started"
        let mutable currentBackoffMs = VpnTunnelService.HealthCheckIntervalMs
        let mutable authFailedOnce = false

        while not token.IsCancellationRequested do
            try
                match getAuth() with
                | None ->
                    // No auth - need to authenticate (Connecting or Reconnecting state)
                    // Spec 050: Re-query network info BEFORE each authentication attempt during Reconnecting
                    let currentState = getState()
                    if currentState = VpnServiceConnectionState.Reconnecting then
                        Logger.logInfo "Reconnecting: refreshing network info before auth..."
                        try
                            let _ = getNetworkType()
                            let _ = getPhysicalInterfaceName()
                            let _ = getPhysicalGatewayIp()
                            Logger.logInfo "Network info refreshed successfully"
                        with
                        | ex ->
                            setLastError $"Network info refresh failed: {ex.Message}"
                            // Continue anyway - will retry on next iteration

                    match authClient with
                    | Some client ->
                        match authenticate client serviceData.clientAccessInfo.vpnClientId hash with
                        | Ok response ->
                            // Atomically swap the auth snapshot
                            setAuth (Some response)
                            authFailedOnce <- false
                            currentBackoffMs <- VpnTunnelService.HealthCheckIntervalMs
                            // Transition to Connected
                            setState VpnServiceConnectionState.Connected
                            Logger.logInfo $"VPN connected with sessionId: {response.sessionId.value}"
                        | Error _ ->
                            // Log auth failure once, then occasionally
                            if not authFailedOnce then
                                Logger.logWarn "Authentication failed, will retry with backoff"
                                authFailedOnce <- true
                            // Exponential backoff
                            Thread.Sleep(currentBackoffMs)
                            currentBackoffMs <- min (currentBackoffMs * 2) VpnTunnelService.MaxHealthCheckBackoffMs
                    | None ->
                        // No auth client - should not happen, but backoff
                        Thread.Sleep(currentBackoffMs)

                | Some auth ->
                    // Have auth - do health check
                    Thread.Sleep(currentBackoffMs)
                    if not token.IsCancellationRequested then
                        match authClient with
                        | Some client ->
                            let sessionValid = pingSession client serviceData.clientAccessInfo.vpnClientId auth
                            if sessionValid then
                                // Reset backoff on success
                                currentBackoffMs <- VpnTunnelService.HealthCheckIntervalMs
                            else
                                // Session expired or ping failed - need to re-authenticate
                                Logger.logInfo "Session expired, entering Reconnecting state..."
                                setState VpnServiceConnectionState.Reconnecting
                                // Clear auth to trigger re-authentication on the next iteration
                                // UDP loops will skip when getAuth() returns None (spec 050)
                                setAuth None
                                currentBackoffMs <- VpnTunnelService.HealthCheckIntervalMs
                        | None -> ()
            with
            | :? ThreadInterruptedException -> ()
            | ex when not token.IsCancellationRequested ->
                // Never exit on exceptions, just log and continue (same as Windows)
                setLastError $"Supervisor loop error: {ex.Message}"
                Thread.Sleep(currentBackoffMs)
                currentBackoffMs <- min (currentBackoffMs * 2) VpnTunnelService.MaxHealthCheckBackoffMs

        Logger.logInfo "Supervisor loop stopped"

    /// Set the Android context. Must be called before StartVpn().
    member _.SetContext(ctx: Context) =
        context <- ctx

    member this.StartVpn(serviceData: VpnClientServiceData) : bool =
        try
            if isNull context then
                Logger.logError "Context not set. Call SetContext() before StartVpn()."
                setState (VpnServiceConnectionState.Failed "Context not set")
                false
            else

            Logger.logInfo "Starting VPN service..."
            setState VpnServiceConnectionState.Connecting
            clearLastError ()

            // Spec 056: Compute client hash from Android Secure ID
            match getAndroidClientHash context with
            | Error e ->
                let errMsg = $"Failed to get client hash: {e}"
                Logger.logError errMsg
                setLastError errMsg
                setState VpnServiceConnectionState.Disconnected
                false
            | Ok hash ->

            // Store hash for supervisor loop
            clientHash <- Some hash

            // Create auth client and authenticate
            let auth = createAuthWcfClient serviceData
            authClient <- Some auth

            let authRequest =
                {
                    clientId = serviceData.clientAccessInfo.vpnClientId
                    clientHash = hash
                    timestamp = DateTime.UtcNow
                    nonce = Guid.NewGuid().ToByteArray()
                }

            match auth.authenticate authRequest with
            | Ok response ->
                setAuth (Some response)
                Logger.logInfo $"Authentication successful, sessionId: {response.sessionId.value}"

                // Build VPN interface
                let builder = new VpnService.Builder(this)
                builder.SetSession("Softellect VPN") |> ignore
                builder.SetMtu(VpnTunnelService.TunnelMtu) |> ignore
                builder.AddAddress(response.assignedIp.value.value, 24) |> ignore
                builder.AddRoute("0.0.0.0", 0) |> ignore
                // Exclude the VPN app itself from the tunnel
                builder.AddDisallowedApplication(context.PackageName) |> ignore

                vpnInterface <- builder.Establish()
                if vpnInterface = null then
                    let errMsg = "Failed to establish VPN interface"
                    Logger.logError errMsg
                    setLastError errMsg
                    setState VpnServiceConnectionState.Disconnected
                    false
                else
                    try
                        tunInputStream <- new FileInputStream(vpnInterface.FileDescriptor)
                        tunOutputStream <- new FileOutputStream(vpnInterface.FileDescriptor)

                        // Create UDP client
                        let udp = createVpnPushUdpClient serviceData getAuth
                        udpClient <- Some udp

                        // Set packet injector for direct injection
                        udp.setPacketInjector(createPacketInjector tunOutputStream)

                        // Start UDP client
                        udp.start()

                        // Start cancellation token
                        cts <- new CancellationTokenSource()
                        let token = cts.Token

                        // Start TUN read thread
                        tunReadThread <- new Thread(fun () -> tunReadLoop tunInputStream udp token)
                        tunReadThread.Name <- "TUN-Read"
                        tunReadThread.Start()

                        // Start TUN write thread (for packets not directly injected)
                        tunWriteThread <- new Thread(fun () -> tunWriteLoop tunOutputStream udp token)
                        tunWriteThread.Name <- "TUN-Write"
                        tunWriteThread.Start()

                        // Start supervisor thread (handles health checks and reconnection - spec 050)
                        // Spec 056: Pass hash to supervisor loop for device binding
                        supervisorThread <- new Thread(fun () -> supervisorLoop serviceData hash token)
                        supervisorThread.Name <- "Supervisor"
                        supervisorThread.Start()

                        // Set initial Connected state
                        setState VpnServiceConnectionState.Connected
                        Logger.logInfo "VPN service started successfully"
                        true
                    with
                    | e ->
                        let errMsg = $"VPN service - exception during setup: '%A{e}'."
                        Logger.logError errMsg
                        setLastError errMsg
                        setState VpnServiceConnectionState.Disconnected
                        closeVpnInterface ()
                        false

            | Error e ->
                let errMsg = $"Authentication failed: %A{e}"
                Logger.logError errMsg
                setLastError errMsg
                setState VpnServiceConnectionState.Disconnected
                false
        with
        | ex ->
            let errMsg = $"Failed to start VPN: {ex.Message}"
            Logger.logError errMsg
            setLastError errMsg
            setState VpnServiceConnectionState.Disconnected
            false

    member this.StopVpn() =
        Logger.logInfo "Stopping VPN service..."

        // Cancel all threads
        if cts <> null then
            cts.Cancel()

        // Stop UDP client
        match udpClient with
        | Some udp ->
            udp.stop()
            (udp :> IDisposable).Dispose()
            udpClient <- None
        | None -> ()

        // Wait for threads
        let waitThread (t: Thread) =
            if t <> null && t.IsAlive then
                try t.Join(TimeSpan.FromSeconds(2.0)) |> ignore with | _ -> ()

        waitThread tunReadThread
        waitThread tunWriteThread
        waitThread supervisorThread

        tunReadThread <- null
        tunWriteThread <- null
        supervisorThread <- null

        // Close streams
        if tunInputStream <> null then
            try tunInputStream.Close() with | _ -> ()
            tunInputStream <- null

        if tunOutputStream <> null then
            try tunOutputStream.Close() with | _ -> ()
            tunOutputStream <- null

        // Close VPN interface
        closeVpnInterface()

        // Clear auth
        setAuth None
        authClient <- None

        // Reset stats
        bytesSent <- 0L
        bytesReceived <- 0L
        packetsSent <- 0L
        packetsReceived <- 0L

        if cts <> null then
            cts.Dispose()
            cts <- null

        // Set state to Disconnected
        setState VpnServiceConnectionState.Disconnected
        Logger.logInfo "VPN service stopped"

    /// Get current stats.
    member _.GetStats() =
        (Interlocked.Read(&bytesSent),
         Interlocked.Read(&bytesReceived),
         Interlocked.Read(&packetsSent),
         Interlocked.Read(&packetsReceived))

    /// Check if VPN is running.
    member _.IsRunning =
        vpnInterface <> null && cts <> null && not cts.IsCancellationRequested

    /// Get current session ID (1 byte).
    member _.SessionId : byte =
        match getAuth() with
        | Some auth -> auth.sessionId.value
        | None -> 0uy

    /// Get current connection state (spec 050).
    member _.State : VpnServiceConnectionState =
        getState()

    /// Get last error message (spec 050).
    member _.LastError : string =
        getLastError()

    override this.OnStartCommand(intent: Intent, flags: StartCommandFlags, startId: int) =
        StartCommandResult.Sticky

    override this.OnDestroy() =
        this.StopVpn()
        base.OnDestroy()
