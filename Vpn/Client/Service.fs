namespace Softellect.Vpn.Client

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Wcf.Common
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Client.Tunnel
open Softellect.Vpn.Client.WcfClient
open Softellect.Vpn.Client.UdpClient
open Softellect.Vpn.Interop

module Service =

    [<Literal>]
    let MaxSendPacketsPerCall = 64

    [<Literal>]
    let MaxSendBytesPerCall = 65536

    [<Literal>]
    let ReceiveEmptyBackoffMs = 10

    /// Health check interval in ms (spec 042: 30 seconds between successful checks).
    [<Literal>]
    let HealthCheckIntervalMs = 30000

    /// Maximum health check backoff in ms (spec 042: cap at 5 minutes).
    [<Literal>]
    let MaxHealthCheckBackoffMs = 300000


    type VpnClientConnectionState =
        | Disconnected
        | Connecting
        | Connected of VpnIpAddress
        | Reconnecting
        | Failed of string


    let private getServerIp (data: VpnClientServiceData) =
        match data.clientAccessInfo.serverAccessInfo with
        | NetTcpServiceInfo info -> info.netTcpServiceAddress.value.ipAddress
        | HttpServiceInfo info -> info.httpServiceAddress.value.ipAddress


    let private getServerIpAddress (data: VpnClientServiceData) =
        match data.clientAccessInfo.serverAccessInfo with
        | NetTcpServiceInfo info -> info.netTcpServiceAddress.value
        | HttpServiceInfo info -> info.httpServiceAddress.value


    let private getServerPort (data: VpnClientServiceData) =
        match data.clientAccessInfo.serverAccessInfo with
        | NetTcpServiceInfo info -> info.netTcpServicePort.value
        | HttpServiceInfo info -> info.httpServicePort.value


    let private enableKillSwitch (data: VpnClientServiceData)=
        // Logger.logInfo "Kill-switch is turned off..."
        // Ok ()

        Logger.logInfo "Enabling kill-switch..."
        let ks = new KillSwitch()
        let serverIp = getServerIp data
        let serverPort = getServerPort data
        let exclusions = data.clientAccessInfo.localLanExclusions |> List.map (fun e -> e.value)

        let result = ks.Enable(serverIp, serverPort, exclusions)

        if result.IsSuccess then
            Logger.logInfo "Kill-switch enabled"
            Ok ks
        else
            let errMsg = match result.Error with | null -> "Unknown error" | e -> e
            Logger.logError $"Failed to enable kill-switch: {errMsg}"
            ks.Dispose()
            Error errMsg


    let private disableKillSwitch (killSwitch : KillSwitch option) =
        match killSwitch with
        | Some ks ->
            Logger.logInfo "Disabling kill-switch..."
            ks.Disable() |> ignore
            ks.Dispose()
            Logger.logInfo "Kill-switch disabled"
        | None -> ()


    let getTunnelConfig (data: VpnClientServiceData) gatewayIp assignedIp =
        {
            adapterName = AdapterName
            assignedIp = assignedIp
            subnetMask = IpAddress.subnetMask24
            gatewayIp = gatewayIp
            dnsServerIp = gatewayIp
            serverPublicIp = getServerIpAddress data
            physicalGatewayIp = data.clientAccessInfo.physicalGatewayIp
            physicalInterfaceName = data.clientAccessInfo.physicalInterfaceName
        }


    /// Tunnel wrapper that implements IPacketInjector for a push client.
    type TunnelInjector(tunnel: Tunnel) =
        interface IPacketInjector with
            member _.injectPacket(packet) = tunnel.injectPacket(packet)


    /// VPN client service using push dataplane (spec 042).
    /// Implements client resilience via atomic auth snapshot.
    /// - StartAsync is lightweight and non-blocking
    /// - Supervisor loop handles auth, health checks, and recovery
    /// - UDP plane runs once and never restarts
    /// - Client never exits due to transient failures
    type VpnPushClientService(data: VpnClientServiceData) =
        let mutable connectionState = Disconnected
        let mutable tunnel : Tunnel option = None
        let mutable killSwitch : KillSwitch option = None
        let mutable pushClient : VpnPushUdpClient option = None
        let mutable sendTask : Task option = None
        let mutable supervisorTask : Task option = None
        let mutable running = false
        let cts = new CancellationTokenSource()

        // Spec 042: Single mutable auth snapshot - The source of truth for UDP plane
        let mutable currentAuth : VpnAuthResponse option = None
        let authLock = obj()

        /// Atomically get the current auth snapshot.
        let getAuth () =
            lock authLock (fun () -> currentAuth)

        /// Atomically swap the auth snapshot.
        let setAuth (auth: VpnAuthResponse option) =
            lock authLock (fun () -> currentAuth <- auth)

        let authenticate () =
            Logger.logInfo "Push: Authenticating with server..."
            let authClient = createAuthWcfClient data

            let request : VpnAuthRequest =
                {
                    clientId = data.clientAccessInfo.vpnClientId
                    timestamp = DateTime.UtcNow
                    nonce = Guid.NewGuid().ToByteArray()
                }

            try
                match authClient.authenticate request with
                | Ok response ->
                    Logger.logInfo $"Push: Authenticated successfully. Assigned IP: {response.assignedIp.value}, SessionId: {response.sessionId.value}"
                    Ok response
                | Error e ->
                    Logger.logWarn $"Push: Authentication failed: '%A{e}'"
                    Error $"Authentication error: '%A{e}'."
            with
            | ex ->
                Logger.logWarn $"Push: Authentication exception: {ex.Message}"
                Error $"Authentication exception: {ex.Message}"

        /// Ping the server to check if the session is still valid.
        let pingSession (auth: VpnAuthResponse) =
            let authClient = createAuthWcfClient data

            let request : VpnPingRequest =
                {
                    clientId = data.clientAccessInfo.vpnClientId
                    sessionId = auth.sessionId
                    timestamp = DateTime.UtcNow
                }

            try
                match authClient.pingSession request with
                | Ok () -> true
                | Error _ -> false
            with
            | _ -> false

        let startTunnel (assignedIp: VpnIpAddress) =
            Logger.logInfo $"Starting tunnel with assignedIp: '{assignedIp.value.value}'."
            let gatewayIp = serverVpnIp.value
            let config = getTunnelConfig data gatewayIp assignedIp
            let t = Tunnel(config, cts.Token)

            match t.start() with
            | Ok () ->
                tunnel <- Some t
                Ok t
            | Error msg ->
                Error msg

        /// Send loop for push dataplane - reads from the tunnel and enqueues to the push client.
        let sendLoopAsync (t: Tunnel) (pc: VpnPushUdpClient) =
            task {
                while running && not cts.Token.IsCancellationRequested do
                    try
                        // Wait for at least one packet using ReadAsync.
                        let! firstPacket = t.outboundPacketReader.ReadAsync(cts.Token).AsTask()

                        // Enqueue immediately (push semantics).
                        pc.enqueueOutbound(firstPacket) |> ignore

                        // Drain additional packets without blocking.
                        let mutable hasMore = true
                        while hasMore do
                            match t.outboundPacketReader.TryRead() with
                            | true, packet ->
                                pc.enqueueOutbound(packet) |> ignore
                            | false, _ -> hasMore <- false
                    with
                    | :? OperationCanceledException -> ()
                    | ex ->
                        // Spec 042: Don't exit on errors, just log
                        Logger.logError $"Push: Error in send loop: {ex.Message}"
            } :> Task

        /// Spec 042: Supervisor loop - handles authentication and health checks.
        /// Runs forever until stopped. Never exits due to transient failures.
        let supervisorLoopAsync () =
            task {
                let mutable tunnelStarted = false
                let mutable udpStarted = false
                let mutable currentBackoffMs = HealthCheckIntervalMs
                let mutable authFailedOnce = false

                while running && not cts.Token.IsCancellationRequested do
                    try
                        match getAuth() with
                        | None ->
                            // No auth - need to authenticate
                            connectionState <- Connecting

                            match authenticate() with
                            | Ok authResponse ->
                                // Atomically swap the auth snapshot
                                setAuth (Some authResponse)
                                authFailedOnce <- false
                                currentBackoffMs <- HealthCheckIntervalMs

                                // Start the tunnel if not started (first successful auth)
                                if not tunnelStarted then
                                    match startTunnel authResponse.assignedIp with
                                    | Ok t ->
                                        Logger.logInfo $"Push: Tunnel started with IP: '{authResponse.assignedIp.value.value}', state: '%A{t.state}'."

                                        // Permit traffic from VPN local address in kill-switch
                                        match killSwitch with
                                        | Some ks ->
                                            let r = ks.AddPermitFilterForLocalHost(authResponse.assignedIp.value.ipAddress, $"Permit VPN Local {authResponse.assignedIp.value}")
                                            if r.IsSuccess then
                                                Logger.logInfo $"Kill-switch: permitted VPN local address {authResponse.assignedIp.value}"
                                                tunnelStarted <- true
                                            else
                                                let errMsg = match r.Error with | null -> "Unknown error" | e -> e
                                                Logger.logError $"Failed to permit VPN local address in kill-switch: {errMsg}"
                                        | None -> Logger.logError "Kill-switch instance is missing"
                                    | Error msg -> Logger.logError $"Push: Failed to start tunnel: {msg}"

                                // Start a UDP client if the tunnel started and UDP not started
                                if tunnelStarted && not udpStarted then
                                    match tunnel with
                                    | Some t ->
                                        Logger.logInfo "Creating and starting push UDP client."
                                        let pc = createVpnPushUdpClient data getAuth

                                        Logger.logInfo "Setting up direct injection from push client to tunnel."
                                        pc.setPacketInjector(TunnelInjector(t))

                                        Logger.logInfo "Starting push client loops."
                                        pc.start()
                                        pushClient <- Some pc

                                        Logger.logInfo "Starting send loop (tunnel -> push client)."
                                        sendTask <- Some (Task.Run(fun () -> sendLoopAsync t pc))

                                        udpStarted <- true
                                    | None -> ()

                                if tunnelStarted && udpStarted then
                                    connectionState <- Connected authResponse.assignedIp
                                    Logger.logInfo $"Push VPN Client connected with IP: {authResponse.assignedIp.value}"

                            | Error _ ->
                                // Spec 042: Log auth failure once, then info occasionally
                                if not authFailedOnce then
                                    Logger.logWarn "Push: Authentication failed, will retry with backoff"
                                    authFailedOnce <- true

                                // Exponential backoff
                                do! Task.Delay(currentBackoffMs, cts.Token)
                                currentBackoffMs <- min (currentBackoffMs * 2) MaxHealthCheckBackoffMs

                        | Some auth ->
                            // Have auth - do health check
                            do! Task.Delay(currentBackoffMs, cts.Token)

                            let sessionValid = pingSession auth

                            if sessionValid then
                                // Reset backoff on success
                                currentBackoffMs <- HealthCheckIntervalMs
                            else
                                // Session expired - need to re-authenticate
                                Logger.logInfo "Push: Session expired, re-authenticating..."
                                connectionState <- Reconnecting
                                // Clear auth to trigger re-authentication on the next iteration
                                // Spec 042: Do NOT stop UDP - it will just skip when getAuth() returns None
                                setAuth None
                                currentBackoffMs <- HealthCheckIntervalMs
                    with
                    | :? OperationCanceledException -> ()
                    | ex ->
                        // Spec 042: Never exit on exceptions, just log and continue
                        Logger.logError $"Push: Supervisor loop error: {ex.Message}"
                        do! Task.Delay(currentBackoffMs, cts.Token)
                        currentBackoffMs <- min (currentBackoffMs * 2) MaxHealthCheckBackoffMs
            } :> Task

        interface IHostedService with
            /// Spec 042: StartAsync MUST be lightweight and non-blocking.
            /// Only enables a kill-switch and starts the supervisor loop.
            member _.StartAsync(cancellationToken: CancellationToken) =
                Logger.logInfo $"Starting Push VPN Client Service (spec 042), useEncryption: {data.clientAccessInfo.useEncryption}, encryptionType: %A{data.clientAccessInfo.encryptionType}..."

                // Enable kill-switch FIRST - this is a critical startup requirement
                match enableKillSwitch data with
                | Ok ks ->
                    killSwitch <- Some ks
                    running <- true

                    // Initialize currentAuth = None (spec 042)
                    setAuth None

                    // Start supervisor loop in background (spec 042)
                    Logger.logInfo "Starting supervisor loop..."
                    supervisorTask <- Some (Task.Run(supervisorLoopAsync))

                    // Return immediately (spec 042: non-blocking)
                    Task.CompletedTask

                | Error msg ->
                    // Kill-switch failure is a CRITICAL startup failure (spec 042)
                    connectionState <- Failed msg
                    Logger.logError $"Push: Failed to enable kill-switch: {msg}"
                    Task.FromException(Exception($"Failed to enable kill-switch: {msg}"))

            member _.StopAsync(cancellationToken: CancellationToken) =
                Logger.logInfo "Stopping Push VPN Client Service..."
                running <- false
                cts.Cancel()

                // Wait for the supervisor task
                match supervisorTask with
                | Some t ->
                    try t.Wait(TimeSpan.FromSeconds(5.0)) |> ignore with | _ -> ()
                    supervisorTask <- None
                | None -> ()

                // Wait for a send task
                match sendTask with
                | Some t ->
                    try t.Wait(TimeSpan.FromSeconds(5.0)) |> ignore with | _ -> ()
                    sendTask <- None
                | None -> ()

                // Stop and dispose push client
                match pushClient with
                | Some pc ->
                    pc.stop()
                    (pc :> IDisposable).Dispose()
                    pushClient <- None
                | None -> ()

                // Stop tunnel
                match tunnel with
                | Some t ->
                    t.stop()
                    tunnel <- None
                | None -> ()

                // Disable kill-switch LAST
                disableKillSwitch killSwitch
                killSwitch <- None

                connectionState <- Disconnected
                Logger.logInfo "Push VPN Client Service stopped"
                Task.CompletedTask

        member _.state = connectionState
        member _.isKillSwitchActive = killSwitch.IsSome && killSwitch.Value.IsEnabled
