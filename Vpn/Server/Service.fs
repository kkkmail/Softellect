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

    /// Maximum number of packets to drain per receivePackets call.
    [<Literal>]
    let MaxReceivePacketsPerCall = 256


    /// Maximum number of packets to drain per receivePackets call.
    [<Literal>]
    let MaxSendPacketsPerCall = 256


    type AuthService(data: VpnServerData) =
        let mutable started = false

        let registryData : ClientRegistryData =
            {
                serverAccessInfo = data.serverAccessInfo
                serverPrivateKey = data.serverPrivateKey
                serverPublicKey = data.serverPublicKey
            }

        let registry = ClientRegistry(registryData)
        do Logger.logInfo $"Created registry: {registry.GetHashCode()}."
        let toAuthError f = f |> AuthWcfError |> AuthFailedErr |> ConnectionErr

        /// Verify the timestamp is recent (within 5 minutes)
        let verifyAuthRequest (request: VpnAuthRequest) =
            let timeDiff = DateTime.UtcNow - request.timestamp
            if timeDiff.TotalMinutes > 5.0 then Error (AuthExpiredErr |> AuthFailedErr |> ConnectionErr)
            else Ok ()

        interface IAuthService with
            member _.authenticate request =
                Logger.logInfo $"Authentication request from client: {request.clientId.value}"

                match verifyAuthRequest request with
                | Ok () ->
                    match registry.createPushSession(request.clientId) with
                    | Ok session ->
                        Logger.logInfo $"Successfully created push session in registry: {registry.GetHashCode()} for client: '{request.clientId.value}' with sessionId={session.sessionId.value}."

                        let response =
                            {
                                assignedIp = session.assignedIp
                                serverPublicIp = serverVpnIp
                                sessionId = session.sessionId
                                sessionAesKey = session.sessionAesKey
                            }
                        Ok response
                    | Error e -> Error e
                | Error e -> Error e

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                if started then
                    Logger.logInfo "VPN Service already started"
                    Task.CompletedTask
                else
                    Logger.logInfo "Starting VPN Service..."
                    started <- true
                    Logger.logInfo "VPN Service started successfully"
                    Task.CompletedTask

            member _.StopAsync(cancellationToken: CancellationToken) =
                if not started then
                    Logger.logInfo "VPN Service already stopped"
                    Task.CompletedTask
                else
                    Logger.logInfo "Stopping VPN Service..."
                    started <- false
                    Logger.logInfo "VPN Service stopped"
                    Task.CompletedTask

        member _.clientRegistry = registry


    type VpnPushService(data: VpnServerData, registry : ClientRegistry) =
        let mutable started = false

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

        interface IVpnPushService with

            /// This function is called sendPackets to match the client side.
            /// From the server side it is receiving packets sent by a client.
            member _.sendPackets (sessionId : VpnSessionId, packets) =
                let hasSession = registry.tryGetPushSession(sessionId).IsSome

                if hasSession then
                    registry.updateActivity(sessionId)
                    Logger.logTrace (fun () -> $"Server received {packets.Length} packets from client {sessionId.value}.")
                    Logger.logTracePackets (packets, (fun () -> $"Server received packets from client:  '{sessionId.value}': "))

                    let result =
                        packets
                        |> Array.map (processPacket sessionId)
                        |> List.ofArray
                        |> foldUnitResults VpnError.addError

                    result
                else
                    Logger.logInfo $"Failed to find push session in registry: {registry.GetHashCode()} for session: '{sessionId.value}'."
                    Error (sessionId |> SessionExpiredErr |> ServerErr)

            /// This function is called receivePackets to match the client side.
            /// From the server side it is sending packets to a client.
            member _.receivePackets (sessionId : VpnSessionId) =
                match registry.tryGetPushSession(sessionId) with
                | Some session ->
                    registry.updateActivity(sessionId)
                    let packets = session.pendingPackets.dequeueMany(MaxReceivePacketsPerCall)

                    if packets.Length = 0 then
                        Ok None
                    else
                        let totalBytes = packets |> Array.sumBy (fun p -> p.Length)
                        Logger.logTrace (fun () -> $"receivePackets: drained {packets.Length} packets ({totalBytes} bytes) for session {sessionId.value}")
                        Ok (Some packets)
                | None ->
                    Logger.logInfo $"receivePackets: no push session for session '{sessionId.value}'."
                    Error (sessionId |> SessionExpiredErr |> ServerErr)

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

        member _.clientRegistry = registry
