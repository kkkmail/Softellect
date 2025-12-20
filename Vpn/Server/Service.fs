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

    /// Internal interface for wait-aware packet receive (used by UDP server).
    type IVpnServiceInternal =
        abstract receivePacketsWithWait: VpnClientId * int * int -> Result<byte[][] option, VpnError>


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
                    // Create an auth session.
                    match registry.createSession(request.clientId) with
                    | Ok session ->
                        // Also create push session for push dataplane clients.
                        Logger.logInfo $"Creating push session in registry: {registry.GetHashCode()} for client: '{request.clientId.value}'."

                        match registry.createPushSession(request.clientId) with
                        | Ok p ->
                            Logger.logInfo $"Successfully created push session in registry: {registry.GetHashCode()} for client: '{request.clientId.value}', push session: '%A{p}'."
                        | Error e ->
                            Logger.logInfo $"Failed to create push session in registry: {registry.GetHashCode()} for client: '{request.clientId.value}', error: '%A{e}'."

                        let response =
                            {
                                assignedIp = session.assignedIp
                                serverPublicIp = serverVpnIp
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


    // type VpnService(data: VpnServerData) =
    //     let mutable started = false
    //
    //     let registryData : ClientRegistryData =
    //         {
    //             serverAccessInfo = data.serverAccessInfo
    //             serverPrivateKey = data.serverPrivateKey
    //             serverPublicKey = data.serverPublicKey
    //         }
    //
    //     let registry = ClientRegistry(registryData)
    //
    //     let routerConfig =
    //         {
    //             vpnSubnet = data.serverAccessInfo.vpnSubnet
    //             adapterName = AdapterName
    //             serverVpnIp = serverVpnIp
    //             serverPublicIp = data.serverAccessInfo.serviceAccessInfo.getIpAddress()
    //         }
    //
    //     let router = PacketRouter(routerConfig, registry)
    //
    //     let serverVpnIpUint = ipToUInt32 routerConfig.serverVpnIp.value
    //
    //     let toAuthError f = f |> AuthWcfError |> AuthFailedErr |> ConnectionErr
    //     let toSendError f = f |> ConfigErr
    //     let toReceiveError f = f |> ConfigErr
    //
    //     /// Verify the timestamp is recent (within 5 minutes)
    //     let verifyAuthRequest (request: VpnAuthRequest) =
    //         let timeDiff = DateTime.UtcNow - request.timestamp
    //         if timeDiff.TotalMinutes > 5.0 then Error (AuthExpiredErr |> AuthFailedErr |> ConnectionErr)
    //         else Ok ()
    //
    //     let processPacket clientId packet =
    //         // Check if this is a DNS query to the VPN gateway
    //         match tryParseDnsQuery serverVpnIpUint packet with
    //         | Some (srcIp, srcPort, dnsPayload) ->
    //             // Forward DNS query to upstream and enqueue reply
    //             match forwardDnsQuery serverVpnIpUint srcIp srcPort dnsPayload with
    //             | Some replyPacket ->
    //                 registry.enqueuePacketForClient(clientId, replyPacket) |> ignore
    //                 Logger.logTrace (fun () -> $"DNSPROXY: enqueued reply to client {clientId.value} len={replyPacket.Length}")
    //             | None -> () // Timeout/error already logged in DnsProxy, do not inject into TUN
    //             Ok ()
    //
    //         | None ->
    //             // Non-DNS: route either to VPN clients (inside subnet) or to internet (NAT + external)
    //             match router.routeFromClient(packet) with
    //             | Ok () -> Ok ()
    //             | Error msg -> Error (ConfigErr msg)
    //
    //     interface IVpnService with
    //         member _.authenticate request =
    //             Logger.logInfo $"Authentication request from client: {request.clientId.value}"
    //
    //             match verifyAuthRequest request with
    //             | Ok () ->
    //                 // Create a legacy session for backwards compatibility.
    //                 match registry.createSession(request.clientId) with
    //                 | Ok session ->
    //                     // Also create push session for push dataplane clients.
    //                     registry.createPushSession(request.clientId) |> ignore
    //
    //                     let response =
    //                         {
    //                             assignedIp = session.assignedIp
    //                             serverPublicIp = serverVpnIp
    //                         }
    //                     Ok response
    //                 | Error e -> Error e
    //             | Error e -> Error e
    //
    //         /// This function is called sendPackets to match the client side.
    //         /// From the server side it is receiving packets sent by a client.
    //         member _.sendPackets (clientId, packets) =
    //             // Check for the push session first, then the legacy session.
    //             let hasSession =
    //                 registry.tryGetPushSession(clientId).IsSome ||
    //                 registry.tryGetSession(clientId).IsSome
    //
    //             if hasSession then
    //                 registry.updateActivity(clientId)
    //                 Logger.logTrace (fun () -> $"Server received {packets.Length} packets from client {clientId.value}.")
    //                 Logger.logTracePackets (packets, (fun () -> $"Server received packets from client:  '{clientId.value}': "))
    //
    //                 let result =
    //                     packets
    //                     |> Array.map (processPacket clientId)
    //                     |> List.ofArray
    //                     |> foldUnitResults VpnError.addError
    //
    //                 result
    //             else
    //                 Error (clientId |> SessionExpiredErr |> ServerErr)
    //
    //         /// This function is called receivePackets to match the client side.
    //         /// From the server side it is sending packets to a client.
    //         /// Uses default wait parameters (250ms, 100 packets).
    //         member this.receivePackets clientId =
    //             (this :> IVpnServiceInternal).receivePacketsWithWait(clientId, 250, 100)
    //
    //     interface IVpnServiceInternal with
    //         /// Receive packets with configurable wait and max packet parameters.
    //         member _.receivePacketsWithWait(clientId, maxWaitMs, maxPackets) =
    //             // Clamp parameters per spec.
    //             let clampedMaxPackets = max 1 (min 1024 maxPackets)
    //             let clampedMaxWaitMs = max 0 (min 2000 maxWaitMs)
    //
    //             match registry.tryGetSession(clientId) with
    //             | Some session ->
    //                 registry.updateActivity(clientId)
    //
    //                 // Attempt immediate dequeue up to maxPackets.
    //                 let packets = registry.dequeuePacketsForClient(clientId, clampedMaxPackets)
    //                 if packets.Length > 0 then
    //                     let totalBytes = packets |> Array.sumBy (fun p -> p.Length)
    //                     Logger.logTrace (fun () -> $"Server sending {packets.Length} packets to client: '{clientId.value}', total {totalBytes} bytes")
    //                     Logger.logTracePackets (packets, (fun () -> $"Server sending packet to client:  '{clientId.value}': "))
    //                     Ok (Some packets)
    //                 elif clampedMaxWaitMs > 0 then
    //                     // No packets and wait requested - wait on semaphore.
    //                     session.packetsAvailable.Wait(clampedMaxWaitMs) |> ignore
    //                     let packets2 = registry.dequeuePacketsForClient(clientId, clampedMaxPackets)
    //                     if packets2.Length > 0 then
    //                         let totalBytes = packets2 |> Array.sumBy (fun p -> p.Length)
    //                         Logger.logTrace (fun () -> $"Server sending {packets2.Length} packets to client: '{clientId.value}', total {totalBytes} bytes (after wait)")
    //                         Logger.logTracePackets (packets2, (fun () -> $"Server sending packet to client:  '{clientId.value}': "))
    //                         Ok (Some packets2)
    //                     else
    //                         Ok None
    //                 else
    //                     // No wait requested and no packets.
    //                     Ok None
    //             | None -> Error (clientId |> SessionExpiredErr |> ServerErr)
    //
    //     interface IHostedService with
    //         member _.StartAsync(cancellationToken: CancellationToken) =
    //             if started then
    //                 Logger.logInfo "VPN Service already started"
    //                 Task.CompletedTask
    //             else
    //                 Logger.logInfo "Starting VPN Service..."
    //
    //                 match router.start() with
    //                 | Ok () ->
    //                     started <- true
    //                     Logger.logInfo "VPN Service started successfully"
    //                     Task.CompletedTask
    //                 | Error msg ->
    //                     Logger.logError $"Failed to start VPN Service: {msg}"
    //                     Task.FromException(Exception($"Failed to start VPN Service: {msg}"))
    //
    //         member _.StopAsync(cancellationToken: CancellationToken) =
    //             if not started then
    //                 Logger.logInfo "VPN Service already stopped"
    //                 Task.CompletedTask
    //             else
    //                 Logger.logInfo "Stopping VPN Service..."
    //                 router.stop()
    //                 started <- false
    //                 Logger.logInfo "VPN Service stopped"
    //                 Task.CompletedTask
    //
    //     member _.clientRegistry = registry


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
            /// Uses default wait parameters (250ms, 100 packets).
            member this.receivePackets clientId =
                (this :> IVpnServiceInternal).receivePacketsWithWait(clientId, 250, 100)

        interface IVpnServiceInternal with
            /// Receive packets with configurable wait and max packet parameters.
            member _.receivePacketsWithWait(clientId, maxWaitMs, maxPackets) =
                // Clamp parameters per spec.
                let clampedMaxPackets = max 1 (min 1024 maxPackets)
                let clampedMaxWaitMs = max 0 (min 2000 maxWaitMs)

                match registry.tryGetSession(clientId) with
                | Some session ->
                    registry.updateActivity(clientId)

                    // Attempt immediate dequeue up to maxPackets.
                    let packets = registry.dequeuePacketsForClient(clientId, clampedMaxPackets)
                    if packets.Length > 0 then
                        let totalBytes = packets |> Array.sumBy (fun p -> p.Length)
                        Logger.logTrace (fun () -> $"Server sending {packets.Length} packets to client: '{clientId.value}', total {totalBytes} bytes")
                        Logger.logTracePackets (packets, (fun () -> $"Server sending packet to client:  '{clientId.value}': "))
                        Ok (Some packets)
                    elif clampedMaxWaitMs > 0 then
                        // No packets and wait requested - wait on semaphore.
                        session.packetsAvailable.Wait(clampedMaxWaitMs) |> ignore
                        let packets2 = registry.dequeuePacketsForClient(clientId, clampedMaxPackets)
                        if packets2.Length > 0 then
                            let totalBytes = packets2 |> Array.sumBy (fun p -> p.Length)
                            Logger.logTrace (fun () -> $"Server sending {packets2.Length} packets to client: '{clientId.value}', total {totalBytes} bytes (after wait)")
                            Logger.logTracePackets (packets2, (fun () -> $"Server sending packet to client:  '{clientId.value}': "))
                            Ok (Some packets2)
                        else
                            Ok None
                    else
                        // No wait requested and no packets.
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
            member _.sendPackets (clientId, packets) =
                let hasSession = registry.tryGetPushSession(clientId).IsSome

                if hasSession then
                    registry.updateActivity(clientId)
                    Logger.logTrace (fun () -> $"Server received {packets.Length} packets from client {clientId.value}.")
                    Logger.logTracePackets (packets, (fun () -> $"Server received packets from client:  '{clientId.value}': "))

                    let result =
                        packets
                        |> Array.map (processPacket clientId)
                        |> List.ofArray
                        |> foldUnitResults VpnError.addError

                    result
                else
                    Logger.logInfo $"Failed to find push session in registry: {registry.GetHashCode()} for client: '{clientId.value}'."
                    Error (clientId |> SessionExpiredErr |> ServerErr)

            /// This function is called receivePackets to match the client side.
            /// From the server side it is sending packets to a client.
            /// Uses default wait parameters (250ms, 100 packets).
            member this.receivePackets clientId =
                (this :> IVpnServiceInternal).receivePacketsWithWait(clientId, 250, 100)

        interface IVpnServiceInternal with
            /// Receive packets with configurable wait and max packet parameters.
            member _.receivePacketsWithWait(clientId, maxWaitMs, maxPackets) =
                // Clamp parameters per spec.
                let clampedMaxPackets = max 1 (min 1024 maxPackets)
                let clampedMaxWaitMs = max 0 (min 2000 maxWaitMs)

                match registry.tryGetSession(clientId) with
                | Some session ->
                    registry.updateActivity(clientId)

                    // Attempt immediate dequeue up to maxPackets.
                    let packets = registry.dequeuePacketsForClient(clientId, clampedMaxPackets)
                    if packets.Length > 0 then
                        let totalBytes = packets |> Array.sumBy (fun p -> p.Length)
                        Logger.logTrace (fun () -> $"Server sending {packets.Length} packets to client: '{clientId.value}', total {totalBytes} bytes")
                        Logger.logTracePackets (packets, (fun () -> $"Server sending packet to client:  '{clientId.value}': "))
                        Ok (Some packets)
                    elif clampedMaxWaitMs > 0 then
                        // No packets and wait requested - wait on semaphore.
                        session.packetsAvailable.Wait(clampedMaxWaitMs) |> ignore
                        let packets2 = registry.dequeuePacketsForClient(clientId, clampedMaxPackets)
                        if packets2.Length > 0 then
                            let totalBytes = packets2 |> Array.sumBy (fun p -> p.Length)
                            Logger.logTrace (fun () -> $"Server sending {packets2.Length} packets to client: '{clientId.value}', total {totalBytes} bytes (after wait)")
                            Logger.logTracePackets (packets2, (fun () -> $"Server sending packet to client:  '{clientId.value}': "))
                            Ok (Some packets2)
                        else
                            Ok None
                    else
                        // No wait requested and no packets.
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

        member _.clientRegistry = registry
