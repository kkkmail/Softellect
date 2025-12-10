namespace Softellect.Vpn.Server

open System
open System.Threading
open System.Threading.Tasks
open CoreWCF
open Microsoft.Extensions.Hosting
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.Core
open Softellect.Wcf.Service
open Softellect.Wcf.Common
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Server.ClientRegistry
open Softellect.Vpn.Server.PacketRouter

module Service =

    type VpnServerData =
        {
            serverAccessInfo : VpnServerAccessInfo
            serverPrivateKey : PrivateKey
            serverPublicKey : PublicKey
        }


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
                adapterName = "SoftellectVPN"
                serverVpnIp = "10.66.77.1" |> Ip4 |> VpnIpAddress
            }

        let router = PacketRouter(routerConfig, registry)

        let toAuthError f = f |> AuthWcfError |> AuthFailedErr |> ConnectionErr
        let toSendError f = f |> ConfigErr
        let toReceiveError f = f |> ConfigErr

        let verifyAuthRequest (request: VpnAuthRequest) (clientPublicKey: PublicKey) =
            // Verify timestamp is recent (within 5 minutes)
            let timeDiff = DateTime.UtcNow - request.timestamp
            if timeDiff.TotalMinutes > 5.0 then
                Error (AuthExpiredErr |> AuthFailedErr |> ConnectionErr)
            else
                Ok ()

        interface IVpnService with
            member _.authenticate request =
                Logger.logInfo $"Authentication request from client: {request.clientId.value}"

                match registry.TryGetClientConfig(request.clientId) with
                | Some (config, publicKey) ->
                    match verifyAuthRequest request publicKey with
                    | Ok () ->
                        match registry.CreateSession(request.clientId) with
                        | Ok session ->
                            let response =
                                {
                                    success = true
                                    assignedIp = Some session.assignedIp
                                    serverPublicIp = VpnIpAddress.tryCreate "10.66.77.1"
                                    errorMessage = None
                                }
                            Ok response
                        | Error e -> Error e
                    | Error e -> Error e
                | None ->
                    Logger.logWarn $"Client not registered: {request.clientId.value}"
                    let response =
                        {
                            success = false
                            assignedIp = None
                            serverPublicIp = None
                            errorMessage = Some "Client not registered"
                        }
                    Ok response

            member _.sendPacket (clientId, packet) =
                match registry.TryGetSession(clientId) with
                | Some _ ->
                    registry.UpdateActivity(clientId)
                    Logger.logTrace (fun () -> $"Server received packet from client {clientId.value}, size={packet.Length} bytes")

                    match router.InjectPacket(packet) with
                    | Ok () -> Ok ()
                    | Error msg -> Error (ConfigErr msg)
                | None ->
                    Error (clientId |> SessionExpiredErr |> ServerErr)

            member _.receivePackets clientId =
                match registry.TryGetSession(clientId) with
                | Some _ ->
                    registry.UpdateActivity(clientId)
                    let packets = registry.DequeuePacketsForClient(clientId, 100)
                    if packets.Length > 0 then
                        let totalBytes = packets |> Array.sumBy (fun p -> p.Length)
                        Logger.logTrace (fun () -> $"Server sending {packets.Length} packets to client {clientId.value}, total {totalBytes} bytes")
                        Ok (Some packets)
                    else
                        Ok None
                | None ->
                    Error (clientId |> SessionExpiredErr |> ServerErr)

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                if started then
                    Logger.logInfo "VPN Service already started"
                    Task.CompletedTask
                else
                    Logger.logInfo "Starting VPN Service..."

                    match router.Start() with
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
                    router.Stop()
                    started <- false
                    Logger.logInfo "VPN Service stopped"
                    Task.CompletedTask

        member _.Registry = registry


    [<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, IncludeExceptionDetailInFaults = true)>]
    type VpnWcfService(service: IVpnService) =

        let toAuthWcfError (e: Softellect.Wcf.Errors.WcfError) = e |> AuthWcfErr |> AuthWcfError |> AuthFailedErr |> ConnectionErr
        let toSendWcfError (e: Softellect.Wcf.Errors.WcfError) = e |> SendPacketWcfErr |> fun _ -> ConfigErr "Send error"
        let toReceiveWcfError (e: Softellect.Wcf.Errors.WcfError) = e |> ReceivePacketsWcfErr |> fun _ -> ConfigErr "Receive error"

        interface IVpnWcfService with
            member _.authenticate data =
                tryReply service.authenticate toAuthWcfError data

            member _.sendPacket data =
                tryReply service.sendPacket toSendWcfError data

            member _.receivePackets data =
                tryReply service.receivePackets toReceiveWcfError data
