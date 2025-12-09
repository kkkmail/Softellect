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

        let getServerPort () =
            match data.clientAccessInfo.serverAccessInfo with
            | NetTcpServiceInfo info -> info.netTcpServicePort.value
            | HttpServiceInfo info -> info.httpServicePort.value

        let enableKillSwitch () =
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
            | Ok response when response.success ->
                match response.assignedIp with
                | Some ip ->
                    Logger.logInfo $"Authenticated successfully. Assigned IP: {ip.value}"
                    Ok ip
                | None ->
                    Logger.logError "Authentication succeeded but no IP assigned"
                    Error "No IP assigned"
            | Ok response ->
                let msg = response.errorMessage |> Option.defaultValue "Unknown error"
                Logger.logError $"Authentication failed: {msg}"
                Error msg
            | Error _ ->
                Logger.logError "Authentication error"
                Error "Authentication error"

        let startTunnel (assignedIp: VpnIpAddress) =
            let config =
                {
                    adapterName = "SoftellectVPN"
                    assignedIp = assignedIp
                    subnetMask = IPAddress.Parse("255.255.255.0")
                }

            let t = Tunnel(config)

            match t.Start() with
            | Ok () ->
                tunnel <- Some t
                Ok ()
            | Error msg ->
                Error msg

        let sendLoop () =
            while running do
                try
                    match tunnel with
                    | Some t when t.IsRunning ->
                        let packets = t.DequeueOutboundPackets(50)

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
                    | Some t, Connected _ when t.IsRunning ->
                        match wcfClient.receivePackets data.clientAccessInfo.vpnClientId with
                        | Ok (Some packets) ->
                            for packet in packets do
                                match t.InjectPacket(packet) with
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
                    t.Stop()
                    tunnel <- None
                | None -> ()

                // Disable kill-switch LAST
                disableKillSwitch()

                state <- Disconnected
                Logger.logInfo "VPN Client Service stopped"
                Task.CompletedTask

        member _.State = state
        member _.IsKillSwitchActive = killSwitch.IsSome && killSwitch.Value.IsEnabled
