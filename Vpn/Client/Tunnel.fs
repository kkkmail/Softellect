namespace Softellect.Vpn.Client

open System
open System.Net
open System.Threading
open Softellect.Sys.Logging
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Interop

module Tunnel =

    type TunnelConfig =
        {
            adapterName : string
            assignedIp : VpnIpAddress
            subnetMask : IPAddress
        }


    type TunnelState =
        | Disconnected
        | Connecting
        | Connected
        | TunnelError of string


    type Tunnel(config: TunnelConfig) =
        let mutable adapter : WinTunAdapter option = None
        let mutable state = Disconnected
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
                        else
                            Thread.Sleep(1)
                    with
                    | ex ->
                        Logger.logError $"Error in tunnel receive loop: {ex.Message}"
                        Thread.Sleep(100)
            | _ ->
                Logger.logWarn "Tunnel adapter not ready for receive loop"

        member _.Start() =
            Logger.logInfo $"Starting tunnel with adapter: {config.adapterName}"
            state <- Connecting

            let createResult = WinTunAdapter.Create(config.adapterName, "SoftellectVPN", System.Nullable<Guid>())

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
                        state <- Connected
                        Logger.logInfo $"Tunnel started with IP: {config.assignedIp.value}"
                        Ok ()
                    else
                        let errMsg = getErrorMessage ipResult
                        state <- TunnelError errMsg
                        createResult.Value.Dispose()
                        adapter <- None
                        Error errMsg
                else
                    let errMsg = getErrorMessage sessionResult
                    state <- TunnelError errMsg
                    createResult.Value.Dispose()
                    adapter <- None
                    Error errMsg
            else
                let errMsg =
                    match createResult.Error with
                    | null -> "Unknown error creating adapter"
                    | err -> err
                state <- TunnelError errMsg
                Error errMsg

        member _.Stop() =
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

            state <- Disconnected
            Logger.logInfo "Tunnel stopped"

        member _.InjectPacket(packet: byte[]) =
            match adapter with
            | Some adp when adp.IsSessionActive ->
                let result = adp.SendPacket(packet)
                if result.IsSuccess then Ok ()
                else Error (getErrorMessage result)
            | _ ->
                Error "Tunnel not ready"

        member _.DequeueOutboundPackets(maxPackets: int) =
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

        member _.State = state
        member _.IsRunning = running
