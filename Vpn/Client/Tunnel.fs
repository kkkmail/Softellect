namespace Softellect.Vpn.Client

open System
open System.Threading
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Interop

module Tunnel =

    type TunnelConfig =
        {
            adapterName : string
            assignedIp : VpnIpAddress
            subnetMask : IpAddress
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
        let packetQueue = System.Collections.Concurrent.ConcurrentQueue<byte[]>()

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
                        if not (isNull packet) then
                            packetQueue.Enqueue(packet)
                            Logger.logTrace (fun () -> $"Tunnel captured packet from TUN adapter, size={packet.Length} bytes, packet: '%A{packet}'.")
                        else
                            Thread.Sleep(1)
                    with
                    | ex ->
                        Logger.logError $"Error in tunnel receive loop: {ex.Message}"
                        Thread.Sleep(100)
            | _ ->
                Logger.logWarn "Tunnel adapter not ready for receive loop"

        member _.start() =
            Logger.logInfo $"Starting tunnel with adapter: {config.adapterName}"
            tunnelState <- Connecting

            let createResult = WinTunAdapter.Create(config.adapterName, adapterName, System.Nullable<Guid>())

            if createResult.IsSuccess then
                adapter <- Some createResult.Value

                let sessionResult = createResult.Value.StartSession()

                if sessionResult.IsSuccess then
                    let ipResult = createResult.Value.SetIpAddress(config.assignedIp.value, config.subnetMask)

                    if ipResult.IsSuccess then
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
                        createResult.Value.Dispose()
                        adapter <- None
                        Error errMsg
                else
                    let errMsg = getErrorMessage sessionResult
                    tunnelState <- TunnelError errMsg
                    createResult.Value.Dispose()
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
                    Logger.logTrace (fun () -> $"Tunnel injected packet to TUN adapter, size={packet.Length} bytes")
                    Ok ()
                else Error (getErrorMessage result)
            | _ ->
                Error "Tunnel not ready"

        member _.dequeueOutboundPackets(maxPackets: int) =
            let packets = ResizeArray<byte[]>()
            let mutable count = 0

            while count < maxPackets do
                match packetQueue.TryDequeue() with
                | true, packet ->
                    packets.Add(packet)
                    count <- count + 1
                | false, _ ->
                    count <- maxPackets

            packets.ToArray()

        member _.state = tunnelState
        member _.isRunning = running
