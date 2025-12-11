namespace Softellect.Vpn.Server

open System
open System.Threading
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Interop

module PacketRouter =

    type PacketRouterConfig =
        {
            vpnSubnet : VpnSubnet
            adapterName : string
            serverVpnIp : VpnIpAddress
        }

        static member defaultValue =
            {
                vpnSubnet = VpnSubnet.defaultValue
                adapterName = adapterName
                serverVpnIp =  serverVpnIp
            }


    type PacketRouter(config: PacketRouterConfig, registry: ClientRegistry.ClientRegistry) =
        let mutable adapter : WinTunAdapter option = None
        let mutable running = false
        let mutable receiveThread : Thread option = None

        let parseSubnet (subnet: string) =
            let parts = subnet.Split('/')
            if parts.Length = 2 then
                match IpAddress.tryCreate(parts[0]), Int32.TryParse(parts[1]) with
                | Some ip, (true, prefix) ->
                    let maskBytes =
                        if prefix = 0 then 0u
                        else UInt32.MaxValue <<< (32 - prefix)
                    Some (ip, maskBytes)
                | _ -> None
            else
                None

        let getDestinationIp (packet: byte[]) =
            if packet.Length >= 20 then
                // IPv4: destination IP is at bytes 16-19
                IpAddress.tryCreate $"{packet[16]}.{packet[17]}.{packet[18]}.{packet[19]}"
            else
                None

        let getSourceIp (packet: byte[]) =
            if packet.Length >= 16 then
                // IPv4: source IP is at bytes 12-15
                IpAddress.tryCreate $"{packet[12]}.{packet[13]}.{packet[14]}.{packet[15]}"
            else
                None

        let findClientByIp (ip: IpAddress) =
            registry.getAllSessions()
            |> List.tryFind (fun s -> s.assignedIp.value.Equals(ip))

        let receiveLoop () =
            match adapter with
            | Some adp when adp.IsSessionActive ->
                while running do
                    try
                        let packet = adp.ReceivePacket()
                        if not (isNull packet) then
                            // Packet from the TUN adapter - route to the appropriate client
                            match getDestinationIp packet with
                            | Some destIp ->
                                match getSourceIp packet with
                                | Some srcIp ->
                                    match findClientByIp destIp with
                                    | Some session ->
                                        registry.enqueuePacketForClient(session.clientId, packet) |> ignore
                                        Logger.logTrace (fun () -> $"Routing packet: src={srcIp}, dst={destIp}, size={packet.Length} bytes → client {session.clientId.value}")
                                    | None ->
                                        Logger.logTrace (fun () -> $"No client found for destination IP: {destIp}")
                                | None ->
                                    Logger.logTrace (fun () -> "Could not parse source IP from packet")
                            | None ->
                                Logger.logTrace (fun () -> "Could not parse destination IP from packet")
                        else
                            // No packet available, wait a bit
                            Thread.Sleep(1)
                    with
                    | ex ->
                        Logger.logError $"Error in receive loop: {ex.Message}"
                        Thread.Sleep(100)
            | _ ->
                Logger.logWarn "Adapter not ready for receive loop"

        let getErrorMessage (result: Softellect.Vpn.Interop.Result<Unit>) =
            match result.Error with
            | null -> "Unknown error"
            | err -> err

        let getErrorMessageT (result: Softellect.Vpn.Interop.Result<WinTunAdapter>) =
            match result.Error with
            | null -> "Unknown error"
            | err -> err

        member _.start() =
            Logger.logInfo $"Starting packet router with adapter: {config.adapterName}"

            let createResult = WinTunAdapter.Create(config.adapterName, adapterName, System.Nullable<Guid>())
            if createResult.IsSuccess then
                adapter <- Some createResult.Value

                let sessionResult = createResult.Value.StartSession()
                if sessionResult.IsSuccess then
                    // Set the IP address on the adapter
                    let serverIp = config.serverVpnIp.value
                    let subnetMask = Ip4 "255.255.255.0"

                    let ipResult = createResult.Value.SetIpAddress(serverIp, subnetMask)
                    if ipResult.IsSuccess then
                        running <- true
                        let thread = Thread(ThreadStart(receiveLoop))
                        thread.IsBackground <- true
                        thread.Start()
                        receiveThread <- Some thread
                        Logger.logInfo $"Packet router started with IP: {serverIp}"
                        Ok ()
                    else
                        let errMsg = getErrorMessage ipResult
                        Logger.logError $"Failed to set IP address: {errMsg}"
                        createResult.Value.Dispose()
                        adapter <- None
                        Error $"Failed to set IP address: {errMsg}"
                else
                    let errMsg = getErrorMessage sessionResult
                    Logger.logError $"Failed to start session: {errMsg}"
                    createResult.Value.Dispose()
                    adapter <- None
                    Error $"Failed to start session: {errMsg}"
            else
                let errMsg = getErrorMessageT createResult
                Logger.logError $"Failed to create adapter: {errMsg}"
                Error $"Failed to create adapter: {errMsg}"

        member _.stop() =
            Logger.logInfo "Stopping packet router"
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

            Logger.logInfo "Packet router stopped"

        member _.injectPacket(packet: byte[]) =
            match adapter with
            | Some adp when adp.IsSessionActive ->
                let result = adp.SendPacket(packet)
                if result.IsSuccess then
                    Logger.logTrace (fun () -> $"Injected packet to TUN adapter, size={packet.Length} bytes")
                    Ok ()
                else Error (getErrorMessage result)
            | _ ->
                Error "Adapter not ready"

        member _.isRunning = running
