namespace Softellect.Vpn.Client

open System
open System.Threading
open System.Threading.Channels
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Vpn.Core.PacketDebug
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.UdpProtocol
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


    type Tunnel(config: TunnelConfig, ct: CancellationToken) =
        let mutable adapter : ITunAdapter option = None
        let mutable tunnelState = Disconnected
        let mutable running = false
        let mutable receiveThread : Thread option = None
        let mutable readEventWarningLogged = false

        // Channel for outbound packets (from TUN adapter to VPN server)
        let outboundChannelOptions = UnboundedChannelOptions(SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false)
        let outboundChannel = Channel.CreateUnbounded<byte[]>(outboundChannelOptions)

        let receiveLoop () =
            match adapter with
            | Some adp when adp.IsSessionActive ->
                let readEvent = adp.GetReadWaitHandle()
                match readEvent with
                | null ->
                    if not readEventWarningLogged then
                        Logger.logWarn "Tunnel: GetReadWaitHandle returned null, exiting receive loop"
                        readEventWarningLogged <- true
                | _ ->
                    let waitHandles = [| readEvent; ct.WaitHandle |]
                    while running && not ct.IsCancellationRequested do
                        try
                            let waitResult = WaitHandle.WaitAny(waitHandles)
                            if waitResult = 1 || ct.IsCancellationRequested then
                                // Cancellation signaled - exit loop
                                ()
                            else
                                // readEvent signaled - drain all available packets
                                let mutable hasMore = true
                                let hasNoMore () = hasMore <- false
                                while hasMore do
                                    match adp.ReceivePacket() with
                                    | None -> hasNoMore ()
                                    | Some empty when empty.Length = 0 -> hasNoMore ()
                                    | Some packet ->
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
                        with
                        | ex -> Logger.logError $"Error in tunnel receive loop: {ex.Message}"
            | _ -> Logger.logWarn "Tunnel adapter not ready for receive loop"

        member private _.addHostRoute() =
            let processName = "netsh"
            let deleteCommand = $"interface ipv4 delete route {config.serverPublicIp.value}/32 \"{config.physicalInterfaceName}\" {config.physicalGatewayIp.value}"
            Logger.logInfo $"Executing: '{processName} {deleteCommand}'."

            match WinTunAdapter.RunCommand(processName, deleteCommand, "delete server /32 exclusion route") with
            | Error errMsg -> Logger.logWarn $"Failed to execute: '{processName} {deleteCommand}', error: {errMsg}. Proceeding further."
            | Ok () -> ()

            let command = $"interface ipv4 add route {config.serverPublicIp.value}/32 \"{config.physicalInterfaceName}\" {config.physicalGatewayIp.value} metric=1"
            let operation = "add server /32 exclusion route"
            Logger.logInfo $"Executing: '{processName} {command}'."

            match WinTunAdapter.RunCommand(processName, command, operation) with
            | Error errMsg ->
                Logger.logError $"Failed to execute: '{processName} {command}', error: {errMsg}"
                Error errMsg
            | Ok () ->
                Logger.logInfo $"Successfully executed: '{processName} {command}'."
                Ok ()

        member t.start() =
            Logger.logInfo $"Starting tunnel with adapter: {config.adapterName}"
            tunnelState <- Connecting

            match WinTunAdapter.Create(config.adapterName, AdapterName, System.Nullable<Guid>()) with
            | Ok createResult ->
                adapter <- Some createResult
                let adp = createResult

                match adp.StartSession() with
                |Ok () ->
                    let ipResult = adp.SetIpAddress config.assignedIp.value config.subnetMask
                    let mtuResult = createResult.SetMtu(MtuSize)
                    Logger.logInfo $"Client - ipResult: {ipResult}, mtuResult: {mtuResult}, MTU size: {MtuSize}."

                    match ipResult with
                    | Ok () ->
                        // Configure DNS on the adapter
                        Logger.logInfo $"Setting DNS server to: {config.dnsServerIp.value}"
                        match adp.SetDnsServer(config.dnsServerIp) with
                        | Error errMsg ->
                            Logger.logError $"Failed to set DNS server: {errMsg}"
                            tunnelState <- TunnelError errMsg
                            adp.Dispose()
                            adapter <- None
                            Error errMsg
                        | Ok () ->
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
                                match adp.AddRoute (Ip4 "0.0.0.0") routeMask config.gatewayIp routeMetric with
                                | Error errMsg ->
                                    Logger.logError $"Failed to add route 0.0.0.0/1: {errMsg}"
                                    tunnelState <- TunnelError errMsg
                                    adp.Dispose()
                                    adapter <- None
                                    Error errMsg
                                | Ok () ->
                                    Logger.logInfo $"Adding route 128.0.0.0/1 via {config.gatewayIp.value}"
                                    match adp.AddRoute (Ip4 "128.0.0.0") routeMask config.gatewayIp routeMetric with
                                    | Error errMsg ->
                                        Logger.logError $"Failed to add route 128.0.0.0/1: {errMsg}"
                                        tunnelState <- TunnelError errMsg
                                        adp.Dispose()
                                        adapter <- None
                                        Error errMsg
                                    | Ok () ->
                                        running <- true
                                        let thread = Thread(ThreadStart(receiveLoop))
                                        thread.IsBackground <- true
                                        thread.Start()
                                        receiveThread <- Some thread
                                        tunnelState <- Connected
                                        Logger.logInfo $"Tunnel started with IP: {config.assignedIp.value}"
                                        Ok ()
                    | Error errMsg ->
                        tunnelState <- TunnelError errMsg
                        adp.Dispose()
                        adapter <- None
                        Error errMsg
                | Error errMsg ->
                    tunnelState <- TunnelError errMsg
                    adp.Dispose()
                    adapter <- None
                    Error errMsg
            | Error errMsg ->
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
                match adp.SendPacket(packet) with
                | Ok () ->
                    Logger.logTrace (fun () -> $"Tunnel injected packet to TUN adapter, size={packet.Length} bytes, packet=%A{(summarizePacket packet)}")
                    Ok ()
                | Error e -> Error e
            | _ -> Error "Tunnel not ready"

        /// Get the channel reader for outbound packets.
        /// Consumers should use ReadAsync to wait, then TryRead to drain.
        member _.outboundPacketReader = outboundChannel.Reader

        member _.state = tunnelState
        member _.isRunning = running
