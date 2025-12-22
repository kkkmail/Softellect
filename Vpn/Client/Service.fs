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
open Softellect.Vpn.Core.PacketDebug

module Service =

    [<Literal>]
    let MaxSendPacketsPerCall = 64

    [<Literal>]
    let MaxSendBytesPerCall = 65536

    [<Literal>]
    let ReceiveEmptyBackoffMs = 10


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


    let private disableKillSwitch (data: VpnClientServiceData) (killSwitch : KillSwitch option) =
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


    /// VPN client service using push dataplane.
    /// Uses push semantics: no polling, server pushes packets directly.
    type VpnPushClientService(data: VpnClientServiceData) =
        let mutable connectionState = Disconnected
        let mutable tunnel : Tunnel option = None
        let mutable killSwitch : KillSwitch option = None
        let mutable pushClient : VpnPushUdpClient option = None
        let mutable sendTask : Task option = None
        let mutable running = false
        let cts = new CancellationTokenSource()

        let authenticate () =
            Logger.logInfo "Push: Authenticating with server..."
            let authClient = createAuthWcfClient data

            let request =
                {
                    clientId = data.clientAccessInfo.vpnClientId
                    timestamp = DateTime.UtcNow
                    nonce = Guid.NewGuid().ToByteArray()
                }

            match authClient.authenticate request with
            | Ok response ->
                Logger.logInfo $"Push: Authenticated successfully. Assigned IP: {response.assignedIp.value}, SessionId: {response.sessionId.value}"
                Ok response
            | Error e ->
                Logger.logError "Push: Authentication error"
                Error $"Authentication error: '%A{e}'."

        let startTunnel (assignedIp: VpnIpAddress) =
            Logger.logInfo $"Starting tunnel with assignedIp: '{assignedIp.value.value}'."
            let gatewayIp = serverVpnIp.value
            let config = getTunnelConfig data gatewayIp assignedIp
            let t = Tunnel(config, cts.Token)

            match t.start() with
            | Ok () ->
                tunnel <- Some t
                Ok ()
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
                        Logger.logError $"Push: Error in send loop: {ex.Message}"
            } :> Task

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                Logger.logInfo "Starting Push VPN Client Service..."

                // Enable kill-switch FIRST.
                match enableKillSwitch data with
                | Ok ks ->
                    killSwitch <- Some ks
                    connectionState <- Connecting

                    match authenticate() with
                    | Ok authResponse ->
                        let assignedIp = authResponse.assignedIp

                        match startTunnel assignedIp with
                        | Ok () ->
                            Logger.logInfo $"Push: Tunnel started with IP: '{assignedIp.value.value}'."

                            // Permit traffic from VPN local address in kill-switch.
                            let assignedIpAddress = assignedIp.value.ipAddress

                            match killSwitch with
                            | Some ks ->
                                let r = ks.AddPermitFilterForLocalHost(assignedIpAddress, $"Permit VPN Local {assignedIp.value}")
                                if r.IsSuccess then
                                    Logger.logInfo $"Kill-switch: permitted VPN local address {assignedIp.value}"
                                else
                                    let errMsg = match r.Error with | null -> "Unknown error" | e -> e
                                    connectionState <- Failed errMsg
                                    Logger.logError $"Failed to permit VPN local address in kill-switch: {errMsg}"
                                    Task.CompletedTask |> ignore
                            | None ->
                                connectionState <- Failed "Kill-switch is not enabled"
                                Logger.logError "Kill-switch instance is missing after Enable()"
                                Task.CompletedTask |> ignore

                            // Only start push client if kill-switch permit succeeded.
                            match connectionState with
                            | Failed _ -> Task.CompletedTask
                            | _ ->

                            running <- true

                            match tunnel with
                            | Some t ->
                                Logger.logInfo "Creating and starting push UDP client."
                                let pc = createVpnPushUdpClient data authResponse.sessionId authResponse.sessionAesKey

                                Logger.logInfo "Setting up direct injection from push client to tunnel."
                                pc.setPacketInjector(TunnelInjector(t))

                                Logger.logInfo "Starting push client loops."
                                pc.start()
                                pushClient <- Some pc

                                Logger.logInfo "Starting send loop (tunnel -> push client) - sendLoopAsync."
                                sendTask <- Some (Task.Run(fun () -> sendLoopAsync t pc))

                                connectionState <- Connected assignedIp
                                Logger.logInfo $"Push VPN Client connected with IP: {assignedIp.value}"
                            | None ->
                                connectionState <- Failed "Push tunnel not available."
                                Logger.logError "Push tunnel not available after start."

                            Task.CompletedTask
                        | Error msg ->
                            connectionState <- Failed msg
                            Logger.logError $"Push: Failed to start tunnel: {msg}"
                            Task.CompletedTask
                    | Error msg ->
                        connectionState <- Failed msg
                        Logger.logError $"Push: Authentication failed: {msg}"
                        Task.CompletedTask
                | Error msg ->
                    connectionState <- Failed msg
                    Logger.logError $"Push: Failed to enable kill-switch: {msg}"
                    Task.FromException(Exception($"Failed to enable kill-switch: {msg}"))

            member _.StopAsync(cancellationToken: CancellationToken) =
                Logger.logInfo "Stopping Push VPN Client Service..."
                running <- false
                cts.Cancel()

                match sendTask with
                | Some t ->
                    try t.Wait(TimeSpan.FromSeconds(5.0)) |> ignore with | _ -> ()
                    sendTask <- None
                | None -> ()

                match pushClient with
                | Some pc ->
                    pc.stop()
                    (pc :> IDisposable).Dispose()
                    pushClient <- None
                | None -> ()

                match tunnel with
                | Some t ->
                    t.stop()
                    tunnel <- None
                | None -> ()
                // Disable kill-switch LAST.
                disableKillSwitch data killSwitch
                killSwitch <- None

                connectionState <- Disconnected
                Logger.logInfo "Push VPN Client Service stopped"
                Task.CompletedTask

        member _.state = connectionState
        member _.isKillSwitchActive = killSwitch.IsSome && killSwitch.Value.IsEnabled
