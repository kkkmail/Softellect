namespace Softellect.Vpn.Client

open System
open System.Net
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Wcf.Common
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Client.Tunnel
open Softellect.Vpn.Client.WcfClient
open Softellect.Vpn.Interop

module Service =

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


    type VpnClientService(data: VpnClientServiceData) =
        let mutable state = Disconnected
        let mutable tunnel : Tunnel option = None
        let mutable killSwitch : KillSwitch option = None
        let mutable sendThread : Thread option = None
        let mutable receiveThread : Thread option = None
        let mutable running = false

        let wcfClient = createVpnClient data.clientAccessInfo

        let getServerIp () =
            match data.clientAccessInfo.serverAccessInfo with
            | NetTcpServiceInfo info -> info.netTcpServiceAddress.value.ipAddress
            | HttpServiceInfo info -> info.httpServiceAddress.value.ipAddress

        let getServerIpAddress () =
            match data.clientAccessInfo.serverAccessInfo with
            | NetTcpServiceInfo info -> info.netTcpServiceAddress.value
            | HttpServiceInfo info -> info.httpServiceAddress.value

        let getServerPort () =
            match data.clientAccessInfo.serverAccessInfo with
            | NetTcpServiceInfo info -> info.netTcpServicePort.value
            | HttpServiceInfo info -> info.httpServicePort.value

        let enableKillSwitch () =
            // Logger.logInfo "Kill-switch is turned off..."
            // Ok ()

            Logger.logInfo "Enabling kill-switch..."
            let ks = new KillSwitch()
            let serverIp = getServerIp()
            let serverPort = getServerPort()
            let exclusions = data.clientAccessInfo.localLanExclusions |> List.map (fun e -> e.value)

            let result = ks.Enable(serverIp, serverPort, exclusions)

            if result.IsSuccess then
                killSwitch <- Some ks
                Logger.logInfo "Kill-switch enabled"
                Ok ()
            else
                let errMsg = match result.Error with | null -> "Unknown error" | e -> e
                Logger.logError $"Failed to enable kill-switch: {errMsg}"
                ks.Dispose()
                Error errMsg

        let disableKillSwitch () =
            match killSwitch with
            | Some ks ->
                Logger.logInfo "Disabling kill-switch..."
                ks.Disable() |> ignore
                ks.Dispose()
                killSwitch <- None
                Logger.logInfo "Kill-switch disabled"
            | None -> ()

        let authenticate () =
            Logger.logInfo "Authenticating with server..."
            let request =
                {
                    clientId = data.clientAccessInfo.vpnClientId
                    timestamp = DateTime.UtcNow
                    nonce = Guid.NewGuid().ToByteArray()
                }

            match wcfClient.authenticate request with
            | Ok response ->
                Logger.logInfo $"Authenticated successfully. Assigned IP: {response.assignedIp.value}"
                Ok response.assignedIp
            | Error e ->
                Logger.logError "Authentication error"
                Error $"Authentication error: '%A{e}'."

        let startTunnel (assignedIp: VpnIpAddress) =
            let gatewayIp = serverVpnIp.value
            let config =
                {
                    adapterName = adapterName
                    assignedIp = assignedIp
                    subnetMask = Ip4 "255.255.255.0"
                    gatewayIp = gatewayIp
                    dnsServerIp = gatewayIp
                    serverPublicIp = getServerIpAddress()
                    physicalGatewayIp = Ip4 "192.168.2.1"
                    physicalInterfaceName = "Wi-Fi"
                }

            let t = Tunnel(config)

            match t.start() with
            | Ok () ->
                tunnel <- Some t
                Ok ()
            | Error msg ->
                Error msg

        let sendLoop () =
            while running do
                try
                    match tunnel with
                    | Some t when t.isRunning ->
                        let packets = t.dequeueOutboundPackets(50)

                        if packets.Length > 0 then
                            let totalBytes = packets |> Array.sumBy (fun p -> p.Length)
                            Logger.logTrace (fun () -> $"Client sending {packets.Length} packets to server, total {totalBytes} bytes")

                        for packet in packets do
                            match wcfClient.sendPacket packet with
                            | Ok () -> ()
                            | Error e ->
                                Logger.logWarn $"Failed to send packet: %A{e}"

                        if packets.Length = 0 then
                            Thread.Sleep(5)
                    | _ ->
                        Thread.Sleep(100)
                with
                | ex ->
                    Logger.logError $"Error in send loop: {ex.Message}"
                    Thread.Sleep(100)

        let receiveLoop () =
            while running do
                try
                    match tunnel, state with
                    | Some t, Connected _ when t.isRunning ->
                        match wcfClient.receivePackets data.clientAccessInfo.vpnClientId with
                        | Ok (Some packets) ->
                            let totalBytes = packets |> Array.sumBy (fun p -> p.Length)
                            Logger.logTrace (fun () -> $"Client received {packets.Length} packets from server, total {totalBytes} bytes")
                            for packet in packets do
                                match t.injectPacket(packet) with
                                | Ok () -> ()
                                | Error msg ->
                                    Logger.logWarn $"Failed to inject packet: {msg}"
                        | Ok None ->
                            Thread.Sleep(10)
                        | Error e ->
                            Logger.logWarn $"Failed to receive packets: %A{e}"
                            Thread.Sleep(100)
                    | _ ->
                        Thread.Sleep(100)
                with
                | ex ->
                    Logger.logError $"Error in receive loop: {ex.Message}"
                    Thread.Sleep(100)

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                Logger.logInfo "Starting VPN Client Service..."

                // Enable kill-switch FIRST (absolute kill-switch requirement)
                match enableKillSwitch() with
                | Ok () ->
                    state <- Connecting

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
                                    state <- Failed errMsg
                                    Logger.logError $"Failed to permit VPN local address in kill-switch: {errMsg}"
                                    Task.CompletedTask |> ignore
                            | None ->
                                state <- Failed "Kill-switch is not enabled"
                                Logger.logError "Kill-switch instance is missing after Enable()"
                                Task.CompletedTask |> ignore

                            // Only start threads if kill-switch permit succeeded
                            match state with
                            | Failed _ -> Task.CompletedTask
                            | _ ->

                            running <- true

                            let st = Thread(ThreadStart(sendLoop))
                            st.IsBackground <- true
                            st.Start()
                            sendThread <- Some st

                            let rt = Thread(ThreadStart(receiveLoop))
                            rt.IsBackground <- true
                            rt.Start()
                            receiveThread <- Some rt

                            state <- Connected assignedIp
                            Logger.logInfo $"VPN Client connected with IP: {assignedIp.value}"
                            Task.CompletedTask
                        | Error msg ->
                            state <- Failed msg
                            Logger.logError $"Failed to start tunnel: {msg}"
                            // Kill-switch remains active - traffic blocked
                            Task.CompletedTask
                    | Error msg ->
                        state <- Failed msg
                        Logger.logError $"Authentication failed: {msg}"
                        // Kill-switch remains active - traffic blocked
                        Task.CompletedTask
                | Error msg ->
                    state <- Failed msg
                    Logger.logError $"Failed to enable kill-switch: {msg}"
                    Task.FromException(Exception($"Failed to enable kill-switch: {msg}"))

            member _.StopAsync(cancellationToken: CancellationToken) =
                Logger.logInfo "Stopping VPN Client Service..."
                running <- false

                match sendThread with
                | Some t ->
                    if t.IsAlive then t.Join(TimeSpan.FromSeconds(5.0)) |> ignore
                    sendThread <- None
                | None -> ()

                match receiveThread with
                | Some t ->
                    if t.IsAlive then t.Join(TimeSpan.FromSeconds(5.0)) |> ignore
                    receiveThread <- None
                | None -> ()

                match tunnel with
                | Some t ->
                    t.stop()
                    tunnel <- None
                | None -> ()

                // Disable kill-switch LAST
                disableKillSwitch()

                state <- Disconnected
                Logger.logInfo "VPN Client Service stopped"
                Task.CompletedTask

        member _.State = state
        member _.IsKillSwitchActive = killSwitch.IsSome && killSwitch.Value.IsEnabled
