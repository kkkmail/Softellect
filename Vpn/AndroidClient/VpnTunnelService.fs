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

module VpnTunnelService =

    /// MTU for VPN tunnel (hardcoded per spec 047).
    [<Literal>]
    let TunnelMtu = 1300

    /// VPN tunnel read buffer size.
    [<Literal>]
    let TunnelBufferSize = 2048


/// Android VpnService implementation for Softellect VPN.
/// Note: This class is used directly (not as a bound service).
/// Call SetContext() before StartVpn() to provide the Android context.
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
    let mutable pingThread: Thread = null

    let mutable bytesSent = 0L
    let mutable bytesReceived = 0L
    let mutable packetsSent = 0L
    let mutable packetsReceived = 0L

    let authLock = obj()

    /// Get current auth response (thread-safe).
    let getAuth () =
        lock authLock (fun () -> authResponse)

    /// Set current auth response (thread-safe).
    let setAuth auth =
        lock authLock (fun () -> authResponse <- auth)

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

    /// Session ping loop - keeps session alive.
    let pingLoop (client: IAuthClient) (clientId: VpnClientId) (token: CancellationToken) =
        Logger.logInfo "Ping loop started"
        let pingInterval = TimeSpan.FromSeconds(30.0)

        while not token.IsCancellationRequested do
            try
                Thread.Sleep(pingInterval)
                if not token.IsCancellationRequested then
                    match getAuth() with
                    | Some auth ->
                        let pingRequest =
                            {
                                clientId = clientId
                                sessionId = auth.sessionId
                                timestamp = DateTime.UtcNow
                            }
                        match client.pingSession pingRequest with
                        | Ok () -> Logger.logTrace (fun () -> "Ping successful")
                        | Error e -> Logger.logWarn $"Ping failed: %A{e}"
                    | None -> ()
            with
            | :? ThreadInterruptedException -> ()
            | ex when not token.IsCancellationRequested ->
                Logger.logError $"Ping error: {ex.Message}"

        Logger.logInfo "Ping loop stopped"

    let closeVpnInterface () =
        // Close VPN interface
        if vpnInterface <> null then
            try vpnInterface.Close() with | _ -> ()
            vpnInterface <- null

    /// Set the Android context. Must be called before StartVpn().
    member _.SetContext(ctx: Context) =
        context <- ctx

    member this.StartVpn(serviceData: VpnClientServiceData) : bool =
        try
            if isNull context then
                Logger.logError "Context not set. Call SetContext() before StartVpn()."
                false
            else

            Logger.logInfo "Starting VPN service..."

            // Create auth client and authenticate
            let auth = createAuthWcfClient serviceData
            authClient <- Some auth

            let authRequest =
                {
                    clientId = serviceData.clientAccessInfo.vpnClientId
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
                    Logger.logError "Failed to establish VPN interface"
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

                        // Start ping thread
                        pingThread <- new Thread(fun () -> pingLoop auth serviceData.clientAccessInfo.vpnClientId token)
                        pingThread.Name <- "Session-Ping"
                        pingThread.Start()

                        Logger.logInfo "VPN service started successfully"
                        true
                    with
                    | e ->
                        Logger.logError $"VPN service - exception during setup: '%A{e}'."
                        closeVpnInterface ()
                        false

            | Error e ->
                Logger.logError $"Authentication failed: %A{e}"
                false
        with
        | ex ->
            Logger.logError $"Failed to start VPN: {ex.Message}"
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
        waitThread pingThread

        tunReadThread <- null
        tunWriteThread <- null
        pingThread <- null

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

    override this.OnStartCommand(intent: Intent, flags: StartCommandFlags, startId: int) =
        StartCommandResult.Sticky

    override this.OnDestroy() =
        this.StopVpn()
        base.OnDestroy()
