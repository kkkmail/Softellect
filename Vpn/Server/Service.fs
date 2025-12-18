namespace Softellect.Vpn.Server

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Softellect.Sys.Logging
open Softellect.Vpn.Core.PacketDebug
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Server.ClientRegistry
open Softellect.Vpn.Server.PacketRouter
open Softellect.Vpn.Server.DnsProxy
open Softellect.Sys.Rop

module Service =

    type VpnService(data: VpnServerData) =
        let mutable started = false

        let registryData : ClientRegistryData =
            {
                serverAccessInfo = data.serverAccessInfo
                serverPrivateKey = data.serverPrivateKey
                serverPublicKey = data.serverPublicKey
            }

        let registry = ClientRegistry(registryData)

        let routerConfig =
            {
                vpnSubnet = data.serverAccessInfo.vpnSubnet
                adapterName = AdapterName
                serverVpnIp = serverVpnIp
                serverPublicIp = data.serverAccessInfo.serviceAccessInfo.getIpAddress()
            }

        let router = PacketRouter(routerConfig, registry)

        let serverVpnIpUint = ipToUInt32 routerConfig.serverVpnIp.value

        let toAuthError f = f |> AuthWcfError |> AuthFailedErr |> ConnectionErr
        let toSendError f = f |> ConfigErr
        let toReceiveError f = f |> ConfigErr

        /// Verify the timestamp is recent (within 5 minutes)
        let verifyAuthRequest (request: VpnAuthRequest) =
            let timeDiff = DateTime.UtcNow - request.timestamp
            if timeDiff.TotalMinutes > 5.0 then Error (AuthExpiredErr |> AuthFailedErr |> ConnectionErr)
            else Ok ()

        let processPacket clientId packet =
            // Check if this is a DNS query to the VPN gateway
            match tryParseDnsQuery serverVpnIpUint packet with
            | Some (srcIp, srcPort, dnsPayload) ->
                // Forward DNS query to upstream and enqueue reply
                match forwardDnsQuery serverVpnIpUint srcIp srcPort dnsPayload with
                | Some replyPacket ->
                    registry.enqueuePacketForClient(clientId, replyPacket) |> ignore
                    Logger.logTrace (fun () -> $"DNSPROXY: enqueued reply to client {clientId.value} len={replyPacket.Length}")
                | None -> () // Timeout/error already logged in DnsProxy, do not inject into TUN
                Ok ()

            | None ->
                // Non-DNS: route either to VPN clients (inside subnet) or to internet (NAT + external)
                match router.routeFromClient(packet) with
                | Ok () -> Ok ()
                | Error msg -> Error (ConfigErr msg)

        interface IVpnService with
            member _.authenticate request =
                Logger.logInfo $"Authentication request from client: {request.clientId.value}"

                match verifyAuthRequest request with
                | Ok () ->
                    match registry.createSession(request.clientId) with
                    | Ok session ->
                        let response =
                            {
                                assignedIp = session.assignedIp
                                serverPublicIp = serverVpnIp
                            }
                        Ok response
                    | Error e -> Error e
                | Error e -> Error e

            /// This function is called sendPackets to match the client side.
            /// From the server side it is receiving packets sent by a client.
            member _.sendPackets (clientId, packets) =
                match registry.tryGetSession(clientId) with
                | Some _ ->
                    registry.updateActivity(clientId)
                    Logger.logTrace (fun () -> $"Server received {packets.Length} packets from client {clientId.value}.")
                    Logger.logTracePackets (packets, (fun () -> $"Server received packets from client:  '{clientId.value}': "))

                    let result =
                        packets
                        |> Array.map (processPacket clientId)
                        |> List.ofArray
                        |> foldUnitResults VpnError.addError

                    result
                | None -> Error (clientId |> SessionExpiredErr |> ServerErr)

            /// This function is called receivePackets to match the client side.
            /// From the server side it is sending packets to a client.
            member _.receivePackets clientId =
                match registry.tryGetSession(clientId) with
                | Some session ->
                    registry.updateActivity(clientId)
                    let packets = registry.dequeuePacketsForClient(clientId, 100)
                    if packets.Length > 0 then
                        let totalBytes = packets |> Array.sumBy (fun p -> p.Length)
                        Logger.logTrace (fun () -> $"Server sending {packets.Length} packets to client: '{clientId.value}', total {totalBytes} bytes")
                        Logger.logTracePackets (packets, (fun () -> $"Server sending packet to client:  '{clientId.value}': "))
                        Ok (Some packets)
                    else
                        // No packets available - wait on semaphore for up to 250 ms
                        session.packetsAvailable.Wait(250) |> ignore
                        let packets2 = registry.dequeuePacketsForClient(clientId, 100)
                        if packets2.Length > 0 then
                            let totalBytes = packets2 |> Array.sumBy (fun p -> p.Length)
                            Logger.logTrace (fun () -> $"Server sending {packets2.Length} packets to client: '{clientId.value}', total {totalBytes} bytes (after wait)")
                            Logger.logTracePackets (packets2, (fun () -> $"Server sending packet to client:  '{clientId.value}': "))
                            Ok (Some packets2)
                        else
                            Ok None
                | None -> Error (clientId |> SessionExpiredErr |> ServerErr)

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                if started then
                    Logger.logInfo "VPN Service already started"
                    Task.CompletedTask
                else
                    Logger.logInfo "Starting VPN Service..."

                    match router.start() with
                    | Ok () ->
                        started <- true
                        Logger.logInfo "VPN Service started successfully"
                        Task.CompletedTask
                    | Error msg ->
                        Logger.logError $"Failed to start VPN Service: {msg}"
                        Task.FromException(Exception($"Failed to start VPN Service: {msg}"))

            member _.StopAsync(cancellationToken: CancellationToken) =
                if not started then
                    Logger.logInfo "VPN Service already stopped"
                    Task.CompletedTask
                else
                    Logger.logInfo "Stopping VPN Service..."
                    router.stop()
                    started <- false
                    Logger.logInfo "VPN Service stopped"
                    Task.CompletedTask

        member _.Registry = registry
