namespace Softellect.Vpn.Client

open Softellect.Sys.Logging
open Softellect.Vpn.Core.PacketDebug
open Softellect.Wcf.Client
open Softellect.Wcf.Errors
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo

module WcfClient =

    let private toAuthError (e: WcfError) = e |> AuthWcfErr |> AuthWcfError |> AuthFailedErr |> ConnectionErr
    let private toSendError (e: WcfError) = e |> SendPacketWcfErr |> fun _ -> ConfigErr "Send packet error"
    let private toReceiveError (e: WcfError) = e |> ReceivePacketsWcfErr |> fun _ -> ConfigErr "Receive packets error"


    type VpnWcfClient(data: VpnClientAccessInfo) =
        let url = data.serverAccessInfo.getUrl()
        let commType = data.serverAccessInfo.communicationType

        do Logger.logInfo $"VpnWcfClient created - URL: '{url}', CommType: '%A{commType}'"
        do Logger.logInfo $"VpnWcfClient - serverAccessInfo: '%A{data.serverAccessInfo}'"

        let getService() =
            Logger.logTrace (fun () -> $"VpnWcfClient.getService - About to call tryGetWcfService with URL: '{url}', CommType: '%A{commType}'")
            let result = tryGetWcfService<IVpnWcfService> commType url
            match result with
            | Ok _ -> Logger.logTrace (fun () -> "VpnWcfClient.getService - Successfully created WCF service proxy")
            | Error e -> Logger.logError $"VpnWcfClient.getService - Failed to create WCF service proxy: %A{e}"
            result

        interface IVpnClient with
            member _.authenticate request =
                Logger.logTrace (fun () -> $"authenticate: Sending auth request for client {request.clientId.value}")
                tryCommunicate getService (fun s b -> s.authenticate b) toAuthError request

            member _.sendPackets packets =
                let clientId = data.vpnClientId
                Logger.logTrace (fun () -> $"Sending {packets.Length} packets for client {clientId.value}.")
                Logger.logTracePackets (packets, (fun () -> $"Sending for client {clientId.value}: "))
                let payload = (clientId, packets)
                let result = tryCommunicate getService (fun s b -> s.sendPackets b) toSendError payload
                Logger.logTrace (fun () -> $"Result: '%A{result}'.")
                result

            member _.receivePackets clientId =
                Logger.logTrace (fun () -> $"receivePackets: Receiving packets for client {clientId.value}")
                let result = tryCommunicate getService (fun s b -> s.receivePackets b) toReceiveError clientId

                match result with
                | Ok (Some r) -> Logger.logTracePackets (r, (fun () -> $"Received for client {clientId.value}: "))
                | Ok None -> Logger.logTrace (fun () -> "Empty response.")
                | Error e -> Logger.logWarn $"ERROR: '{e}'."

                result


    let createVpnWcfClient (clientAccessInfo: VpnClientAccessInfo) : IVpnClient =
        VpnWcfClient(clientAccessInfo) :> IVpnClient
