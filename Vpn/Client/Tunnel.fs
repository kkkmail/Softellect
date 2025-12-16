namespace Softellect.Vpn.Client

open System
open System.Threading
open System.Threading.Channels
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Vpn.Core.PacketDebug
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Interop

module Tunnel =

    type TunnelConfig =
        {
            adapterName : string
            assignedIp : VpnIpAddress
            subnetMask : IpAddress
            gatewayIp : IpAddress
            dnsServerIp : IpAddress
            serverPublicIp : IpAddress
            physicalGatewayIp : IpAddress
            physicalInterfaceName : string
        }


    type TunnelState =
        | Disconnected
        | Connecting
        | Connected
        | TunnelError of string


    type Tunnel(config: TunnelConfig) =
        let mutable adapter : WinTunAdapter option = None
        let mutable tunnelState = Disconnected
        let mutable running = false
        let mutable receiveThread : Thread option = None

        // Channel for outbound packets (from TUN adapter to VPN server)
        let outboundChannelOptions = UnboundedChannelOptions(SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false)
        let outboundChannel = Channel.CreateUnbounded<byte[]>(outboundChannelOptions)

        let getErrorMessage (result: Softellect.Vpn.Interop.Result<Unit>) =
            match result.Error with
            | null -> "Unknown error"
            | err -> err

        let receiveLoop () =
            match adapter with
            | Some adp when adp.IsSessionActive ->
                while running do
                    try
                        let packet = adp.ReceivePacket()
                        if not (isNull packet) && packet.Length > 0 then
                            let v = getIpVersion packet
                            match v with
                            | 4 ->
                                // IPv4 packet - write to channel for sending to VPN server
                                outboundChannel.Writer.TryWrite(packet) |> ignore
                                Logger.logTrace (fun () -> $"Tunnel captured IPv4 packet from TUN adapter, size={packet.Length} bytes, packet=%A{(summarizePacket packet)}")
                            | 6 ->
                                // IPv6 packet - drop (VPN is IPv4-only)
                                Logger.logTrace (fun () -> $"Tunnel: dropping IPv6 packet, len={packet.Length}, packet=%A{(summarizePacket packet)}")
                            | _ ->
                                // Unknown/malformed - drop silently
                                ()
                        // No sleep - tight producer loop
                    with
                    | ex -> Logger.logError $"Error in tunnel receive loop: {ex.Message}"
            | _ -> Logger.logWarn "Tunnel adapter not ready for receive loop"

        member private _.addHostRoute() =
            let processName = "netsh"
            let deleteCommand = $"interface ipv4 delete route {config.serverPublicIp.value}/32 \"{config.physicalInterfaceName}\" {config.physicalGatewayIp.value}"
            Logger.logInfo $"Executing: '{processName} {deleteCommand}'."
            let deleteResult = WinTunAdapter.RunCommand(processName, deleteCommand, "delete server /32 exclusion route");

            if not deleteResult.IsSuccess then
                let errMsg = getErrorMessage deleteResult
                Logger.logWarn $"Failed to execute: '{processName} {deleteCommand}', error: {errMsg}. Proceeding further."

            let command = $"interface ipv4 add route {config.serverPublicIp.value}/32 \"{config.physicalInterfaceName}\" {config.physicalGatewayIp.value} metric=1"
            let operation = "add server /32 exclusion route"
            Logger.logInfo $"Executing: '{processName} {command}'."
            let hostResult = WinTunAdapter.RunCommand(processName, command, operation);
            if not hostResult.IsSuccess then
                let errMsg = getErrorMessage hostResult
                Logger.logError $"Failed to execute: '{processName} {command}', error: {errMsg}"
                Error errMsg
            else
                Logger.logInfo $"Successfully executed: '{processName} {command}'."
                Ok ()

        member t.start() =
            Logger.logInfo $"Starting tunnel with adapter: {config.adapterName}"
            tunnelState <- Connecting

            let createResult = WinTunAdapter.Create(config.adapterName, adapterName, System.Nullable<Guid>())

            if createResult.IsSuccess then
                adapter <- Some createResult.Value
                let adp = createResult.Value

                let sessionResult = adp.StartSession()

                if sessionResult.IsSuccess then
                    let ipResult = adp.SetIpAddress(config.assignedIp.value, config.subnetMask)

                    if ipResult.IsSuccess then
                        // Configure DNS on the adapter
                        Logger.logInfo $"Setting DNS server to: {config.dnsServerIp.value}"
                        let dnsResult = adp.SetDnsServer(config.dnsServerIp)

                        if not dnsResult.IsSuccess then
                            let errMsg = getErrorMessage dnsResult
                            Logger.logError $"Failed to set DNS server: {errMsg}"
                            tunnelState <- TunnelError errMsg
                            adp.Dispose()
                            adapter <- None
                            Error errMsg
                        else
                            match t.addHostRoute() with
                            | Error e ->
                                tunnelState <- TunnelError e
                                adp.Dispose()
                                adapter <- None
                                Error e
                            | Ok () ->
                                // Add split default routes (0.0.0.0/1 and 128.0.0.0/1) through gateway
                                let routeMask = Ip4 "128.0.0.0"
                                let routeMetric = 1

                                Logger.logInfo $"Adding route 0.0.0.0/1 via {config.gatewayIp.value}"
                                let route1Result = adp.AddRoute(Ip4 "0.0.0.0", routeMask, config.gatewayIp, routeMetric)

                                if not route1Result.IsSuccess then
                                    let errMsg = getErrorMessage route1Result
                                    Logger.logError $"Failed to add route 0.0.0.0/1: {errMsg}"
                                    tunnelState <- TunnelError errMsg
                                    adp.Dispose()
                                    adapter <- None
                                    Error errMsg
                                else
                                    Logger.logInfo $"Adding route 128.0.0.0/1 via {config.gatewayIp.value}"
                                    let route2Result = adp.AddRoute(Ip4 "128.0.0.0", routeMask, config.gatewayIp, routeMetric)

                                    if not route2Result.IsSuccess then
                                        let errMsg = getErrorMessage route2Result
                                        Logger.logError $"Failed to add route 128.0.0.0/1: {errMsg}"
                                        tunnelState <- TunnelError errMsg
                                        adp.Dispose()
                                        adapter <- None
                                        Error errMsg
                                    else
                                        running <- true
                                        let thread = Thread(ThreadStart(receiveLoop))
                                        thread.IsBackground <- true
                                        thread.Start()
                                        receiveThread <- Some thread
                                        tunnelState <- Connected
                                        Logger.logInfo $"Tunnel started with IP: {config.assignedIp.value}"
                                        Ok ()
                    else
                        let errMsg = getErrorMessage ipResult
                        tunnelState <- TunnelError errMsg
                        adp.Dispose()
                        adapter <- None
                        Error errMsg
                else
                    let errMsg = getErrorMessage sessionResult
                    tunnelState <- TunnelError errMsg
                    adp.Dispose()
                    adapter <- None
                    Error errMsg
            else
                let errMsg =
                    match createResult.Error with
                    | null -> "Unknown error creating adapter"
                    | err -> err
                tunnelState <- TunnelError errMsg
                Error errMsg

        member _.stop() =
            Logger.logInfo "Stopping tunnel"
            running <- false

            match receiveThread with
            | Some thread ->
                if thread.IsAlive then
                    thread.Join(TimeSpan.FromSeconds(5.0)) |> ignore
                receiveThread <- None
            | None -> ()

            match adapter with
            | Some adp ->
                adp.Dispose()
                adapter <- None
            | None -> ()

            tunnelState <- Disconnected
            Logger.logInfo "Tunnel stopped"

        member _.injectPacket(packet: byte[]) =
            match adapter with
            | Some adp when adp.IsSessionActive ->
                let result = adp.SendPacket(packet)
                if result.IsSuccess then
                    Logger.logTrace (fun () -> $"Tunnel injected packet to TUN adapter, size={packet.Length} bytes, packet=%A{(summarizePacket packet)}")
                    Ok ()
                else Error (getErrorMessage result)
            | _ ->
                Error "Tunnel not ready"

        /// Get the channel reader for outbound packets.
        /// Consumers should use ReadAsync to wait, then TryRead to drain.
        member _.outboundPacketReader = outboundChannel.Reader

        member _.state = tunnelState
        member _.isRunning = running
