namespace Softellect.Vpn.Client

open Softellect.Vpn.Core.ServiceInfo
open Softellect.Sys.Logging
open Softellect.Vpn.Core.PacketDebug

module UdpClient =

    type VpnUdpClient(data: VpnClientAccessInfo) =

        interface IVpnClient with
            member _.authenticate request =
                Logger.logTrace (fun () -> $"authenticate: Sending auth request for client {request.clientId.value}")
                failwith "VpnUdpClient.authenticate is not implemented yet."

            member _.sendPackets packets =
                let clientId = data.vpnClientId
                Logger.logTrace (fun () -> $"Sending {packets.Length} packets for client {clientId.value}.")
                Logger.logTracePackets (packets, (fun () -> $"Sending for client {clientId.value}: "))
                let payload = (clientId, packets)
                let result = failwith "VpnUdpClient.sendPackets is not implemented yet."
                Logger.logTrace (fun () -> $"Result: '%A{result}'.")
                result

            member _.receivePackets clientId =
                Logger.logTrace (fun () -> $"receivePackets: Receiving packets for client {clientId.value}")
                let result = failwith "VpnUdpClient.receivePackets is not implemented yet."

                match result with
                | Ok (Some r) -> Logger.logTracePackets (r, (fun () -> $"Received for client {clientId.value}: "))
                | Ok None -> Logger.logTrace (fun () -> "Empty response.")
                | Error e -> Logger.logWarn $"ERROR: '{e}'."

                result


    let createVpnUdpClient (clientAccessInfo: VpnClientAccessInfo) : IVpnClient =
        VpnUdpClient(clientAccessInfo) :> IVpnClient
