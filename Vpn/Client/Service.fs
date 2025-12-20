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


    type VpnClientServiceData =
        {
            clientAccessInfo : VpnClientAccessInfo
            clientPrivateKey : PrivateKey
            clientPublicKey : PublicKey
            serverPublicKey : PublicKey
        }


    type VpnClientConnectionState =
        | Disconnected
        | Connecting
        | Connected of VpnIpAddress
        | Reconnecting
        | Failed of string


    let createVpnClient (data: VpnClientServiceData) : IVpnClient =
        match data.clientAccessInfo.vpnTransportProtocol with
        | WCF_Tunnel -> createVpnWcfClient data.clientAccessInfo
        | UDP_Tunnel -> createVpnUdpClient data.clientAccessInfo
        // | UDP_Push -> createVpnUdpClient data.clientAccessInfo  // Use legacy UDP for auth only
        | UDP_Push -> failwith $"You are not supposed to call this method for {nameof UDP_Push}."


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


    let private enableKillSwitch (data: VpnClientServiceData) (killSwitch : KillSwitch option ref)=
        // Logger.logInfo "Kill-switch is turned off..."
        // Ok ()

        Logger.logInfo "Enabling kill-switch..."
        let ks = new KillSwitch()
        let serverIp = getServerIp data
        let serverPort = getServerPort data
        let exclusions = data.clientAccessInfo.localLanExclusions |> List.map (fun e -> e.value)

        let result = ks.Enable(serverIp, serverPort, exclusions)

        if result.IsSuccess then
            killSwitch.Value <- Some ks
            Logger.logInfo "Kill-switch enabled"
            Ok ()
        else
            let errMsg = match result.Error with | null -> "Unknown error" | e -> e
            Logger.logError $"Failed to enable kill-switch: {errMsg}"
            ks.Dispose()
            Error errMsg


    let private disableKillSwitch (data: VpnClientServiceData) (killSwitch : KillSwitch option ref) =
        match killSwitch.Value with
        | Some ks ->
            Logger.logInfo "Disabling kill-switch..."
            ks.Disable() |> ignore
            ks.Dispose()
            killSwitch.Value <- None
            Logger.logInfo "Kill-switch disabled"
        | None -> ()


    let getTunnelConfig (data: VpnClientServiceData) gatewayIp assignedIp =
        {
            adapterName = AdapterName
            assignedIp = assignedIp
            subnetMask = Ip4 "255.255.255.0"
            gatewayIp = gatewayIp
            dnsServerIp = gatewayIp
            serverPublicIp = getServerIpAddress data
            physicalGatewayIp = Ip4 "192.168.2.1"
            physicalInterfaceName = "Wi-Fi"
        }


    type VpnClientService(data: VpnClientServiceData) =
        let mutable connectionState = Disconnected
        let mutable tunnel : Tunnel option = None
        let mutable killSwitch : KillSwitch option = None
        let mutable sendTask : Task option = None
        let mutable receiveTask : Task option = None
        let mutable running = false
        let cts = new CancellationTokenSource()

        let vpnClient = createVpnClient data

        let authenticate () =
            Logger.logInfo "Authenticating with server..."
            let request =
                {
                    clientId = data.clientAccessInfo.vpnClientId
                    timestamp = DateTime.UtcNow
                    nonce = Guid.NewGuid().ToByteArray()
                }

            match vpnClient.authenticate request with
            | Ok response ->
                Logger.logInfo $"Authenticated successfully. Assigned IP: {response.assignedIp.value}"
                Ok response.assignedIp
            | Error e ->
                Logger.logError "Authentication error"
                Error $"Authentication error: '%A{e}'."

        let startTunnel (assignedIp: VpnIpAddress) =
            let gatewayIp = serverVpnIp.value
            let config = getTunnelConfig data gatewayIp assignedIp
            let t = Tunnel(config, cts.Token)

            match t.start() with
            | Ok () ->
                tunnel <- Some t
                Ok ()
            | Error msg ->
                Error msg

        let sendLoopAsync (t: Tunnel) =
            task {
                while running && not cts.Token.IsCancellationRequested do
                    try
                        // Wait for at least one packet using ReadAsync
                        let! firstPacket = t.outboundPacketReader.ReadAsync(cts.Token).AsTask()
                        let packets = ResizeArray<byte[]>()
                        packets.Add(firstPacket)
                        let mutable totalBytes = firstPacket.Length

                        // Drain additional packets, but stop when limits are reached
                        let mutable hasMore = true
                        while hasMore && packets.Count < MaxSendPacketsPerCall && totalBytes < MaxSendBytesPerCall do
                            match t.outboundPacketReader.TryRead() with
                            | true, packet ->
                                packets.Add(packet)
                                totalBytes <- totalBytes + packet.Length
                            | false, _ -> hasMore <- false

                        let packetsArray = packets.ToArray()
                        Logger.logTrace (fun () -> $"Client sending {packetsArray.Length} packets to server, total {totalBytes} bytes")
                        Logger.logTracePackets (packetsArray, (fun () -> $"Client sending packet to server: "))

                        match vpnClient.sendPackets packetsArray with
                        | Ok () -> ()
                        | Error e -> Logger.logWarn $"Failed to send {packetsArray.Length} packets to server: %A{e}"
                    with
                    | :? OperationCanceledException -> ()
                    | ex ->
                        Logger.logError $"Error in send loop: {ex.Message}"
            } :> Task

        let receiveLoopAsync (t: Tunnel) =
            task {
                while running && not cts.Token.IsCancellationRequested do
                    try
                        match vpnClient.receivePackets data.clientAccessInfo.vpnClientId with
                        | Ok (Some packets) ->
                            let totalBytes = packets |> Array.sumBy (fun p -> p.Length)
                            Logger.logTrace (fun () -> $"Client received {packets.Length} packets from server, total {totalBytes} bytes")
                            Logger.logTracePackets (packets, (fun () -> $"Client received packet from server: "))

                            for packet in packets do
                                match t.injectPacket(packet) with
                                | Ok () -> ()
                                | Error msg ->
                                    Logger.logWarn $"Failed to inject packet: {msg}"
                        | Ok None ->
                            // No packets available - server long-poll handles the wait.
                            ()
                        | Error e ->
                            Logger.logWarn $"Failed to receive packets: %A{e}"
                    with
                    | :? OperationCanceledException -> ()
                    | ex -> Logger.logError $"Error in receive loop: {ex.Message}"
            } :> Task

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                Logger.logInfo "Starting VPN Client Service..."

                // Enable kill-switch FIRST (absolute kill-switch requirement)
                match enableKillSwitch data (ref killSwitch) with
                | Ok () ->
                    connectionState <- Connecting

                    match authenticate() with
                    | Ok assignedIp ->
                        match startTunnel assignedIp with
                        | Ok () ->
                            Logger.logInfo $"Tunnel started with IP: {assignedIp.value}"

                            // Permit traffic from VPN local address in kill-switch
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

                            // Only start tasks if the kill-switch permit succeeded
                            match connectionState with
                            | Failed _ -> Task.CompletedTask
                            | _ ->

                            running <- true

                            match tunnel with
                            | Some t ->
                                sendTask <- Some (Task.Run(fun () -> sendLoopAsync t))
                                receiveTask <- Some (Task.Run(fun () -> receiveLoopAsync t))
                            | None -> ()

                            connectionState <- Connected assignedIp
                            Logger.logInfo $"VPN Client connected with IP: {assignedIp.value}"
                            Task.CompletedTask
                        | Error msg ->
                            connectionState <- Failed msg
                            Logger.logError $"Failed to start tunnel: {msg}"
                            // Kill-switch remains active - traffic blocked
                            Task.CompletedTask
                    | Error msg ->
                        connectionState <- Failed msg
                        Logger.logError $"Authentication failed: {msg}"
                        // Kill-switch remains active - traffic blocked
                        Task.CompletedTask
                | Error msg ->
                    connectionState <- Failed msg
                    Logger.logError $"Failed to enable kill-switch: {msg}"
                    Task.FromException(Exception($"Failed to enable kill-switch: {msg}"))

            member _.StopAsync(cancellationToken: CancellationToken) =
                Logger.logInfo "Stopping VPN Client Service..."
                running <- false
                cts.Cancel()

                match sendTask with
                | Some t ->
                    try t.Wait(TimeSpan.FromSeconds(5.0)) |> ignore with | _ -> ()
                    sendTask <- None
                | None -> ()

                match receiveTask with
                | Some t ->
                    try t.Wait(TimeSpan.FromSeconds(5.0)) |> ignore with | _ -> ()
                    receiveTask <- None
                | None -> ()

                match tunnel with
                | Some t ->
                    t.stop()
                    tunnel <- None
                | None -> ()

                // Disable kill-switch LAST
                disableKillSwitch data (ref killSwitch)

                connectionState <- Disconnected
                Logger.logInfo "VPN Client Service stopped"
                Task.CompletedTask

        member _.state = connectionState

        member _.isKillSwitchActive =
            match killSwitch with
            | Some k when k.IsEnabled -> true
            | _ -> false


    // ==========================================================================
    // PUSH DATAPLANE CLIENT SERVICE (spec 037)
    // ==========================================================================

    open Softellect.Vpn.Core.UdpProtocol

    /// Tunnel wrapper that implements IPacketInjector for a push client.
    type TunnelInjector(tunnel: Tunnel) =
        interface IPacketInjector with
            member _.InjectPacket(packet) = tunnel.injectPacket(packet)


    /// VPN client service using push dataplane.
    /// Uses push semantics: no polling, server pushes packets directly.
    type VpnPushClientService(data: VpnClientServiceData) =
        let mutable state = Disconnected
        let mutable tunnel : Tunnel option = None
        let mutable killSwitch : KillSwitch option = None
        let mutable pushClient : VpnPushUdpClient option = None
        let mutable sendTask : Task option = None
        let mutable running = false
        let cts = new CancellationTokenSource()

        let authenticate () =
            Logger.logInfo "Push: Authenticating with server..."
            let authClient = createAuthWcfClient data.clientAccessInfo

            let request =
                {
                    clientId = data.clientAccessInfo.vpnClientId
                    timestamp = DateTime.UtcNow
                    nonce = Guid.NewGuid().ToByteArray()
                }

            match authClient.authenticate request with
            | Ok response ->
                Logger.logInfo $"Push: Authenticated successfully. Assigned IP: {response.assignedIp.value}"
                Ok response.assignedIp
            | Error e ->
                Logger.logError "Push: Authentication error"
                Error $"Authentication error: '%A{e}'."

        let startTunnel (assignedIp: VpnIpAddress) =
            let gatewayIp = serverVpnIp.value
            let config = getTunnelConfig data gatewayIp assignedIp
            let t = Tunnel(config, cts.Token)

            match t.start() with
            | Ok () ->
                tunnel <- Some t
                Ok ()
            | Error msg ->
                Error msg

        /// Send loop for push dataplane - reads from the tunnel and enqueues to push client.
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
                match enableKillSwitch data (ref killSwitch) with
                | Ok () ->
                    state <- Connecting

                    match authenticate() with
                    | Ok assignedIp ->
                        match startTunnel assignedIp with
                        | Ok () ->
                            Logger.logInfo $"Push: Tunnel started with IP: {assignedIp.value}"

                            // Permit traffic from VPN local address in kill-switch.
                            let assignedIpAddress = assignedIp.value.ipAddress
                            match killSwitch with
                            | Some ks ->
                                let r = ks.AddPermitFilterForLocalHost(assignedIpAddress, $"Permit VPN Local {assignedIp.value}")
                                if r.IsSuccess then
                                    Logger.logInfo $"Kill-switch: permitted VPN local address {assignedIp.value}"
                                else
                                    let errMsg = match r.Error with | null -> "Unknown error" | e -> e
                                    state <- Failed errMsg
                                    Logger.logError $"Failed to permit VPN local address in kill-switch: {errMsg}"
                                    Task.CompletedTask |> ignore
                            | None ->
                                state <- Failed "Kill-switch is not enabled"
                                Logger.logError "Kill-switch instance is missing after Enable()"
                                Task.CompletedTask |> ignore

                            // Only start push client if kill-switch permit succeeded.
                            match state with
                            | Failed _ -> Task.CompletedTask
                            | _ ->

                            running <- true

                            match tunnel with
                            | Some t ->
                                // Create and start push UDP client.
                                let pc = createVpnPushUdpClient data.clientAccessInfo

                                // Set up direct injection from push client to tunnel.
                                pc.setPacketInjector(TunnelInjector(t))

                                // Start push client loops.
                                pc.start()
                                pushClient <- Some pc

                                // Start send loop (tunnel -> push client).
                                sendTask <- Some (Task.Run(fun () -> sendLoopAsync t pc))

                                state <- Connected assignedIp
                                Logger.logInfo $"Push VPN Client connected with IP: {assignedIp.value}"
                            | None ->
                                state <- Failed "Tunnel not available"
                                Logger.logError "Tunnel not available after start"

                            Task.CompletedTask
                        | Error msg ->
                            state <- Failed msg
                            Logger.logError $"Push: Failed to start tunnel: {msg}"
                            Task.CompletedTask
                    | Error msg ->
                        state <- Failed msg
                        Logger.logError $"Push: Authentication failed: {msg}"
                        Task.CompletedTask
                | Error msg ->
                    state <- Failed msg
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
                disableKillSwitch data (ref killSwitch)

                state <- Disconnected
                Logger.logInfo "Push VPN Client Service stopped"
                Task.CompletedTask

        member _.State = state
        member _.IsKillSwitchActive = killSwitch.IsSome && killSwitch.Value.IsEnabled


    /// Create the appropriate VPN client service based on transport protocol.
    let createVpnClientService (data: VpnClientServiceData) : IHostedService =
        match data.clientAccessInfo.vpnTransportProtocol with
        | WCF_Tunnel | UDP_Tunnel -> VpnClientService(data) :> IHostedService
        | UDP_Push -> VpnPushClientService(data) :> IHostedService
