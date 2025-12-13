namespace Softellect.Vpn.Client

open Softellect.Sys.Logging
open Softellect.Vpn.Core.PacketDebug
open Softellect.Wcf.Client
open Softellect.Wcf.Common
open Softellect.Wcf.Errors
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo

module WcfClient =

    type VpnWcfClientData =
        {
            clientAccessInfo : VpnClientAccessInfo
        }


    let private toAuthError (e: WcfError) = e |> AuthWcfErr |> AuthWcfError |> AuthFailedErr |> ConnectionErr
    let private toSendError (e: WcfError) = e |> SendPacketWcfErr |> fun _ -> ConfigErr "Send packet error"
    let private toReceiveError (e: WcfError) = e |> ReceivePacketsWcfErr |> fun _ -> ConfigErr "Receive packets error"


    type VpnWcfClient(data: VpnWcfClientData) =
        let url = data.clientAccessInfo.serverAccessInfo.getUrl()
        let commType = data.clientAccessInfo.serverAccessInfo.communicationType

        do Logger.logInfo $"VpnWcfClient created - URL: '{url}', CommType: '%A{commType}'"
        do Logger.logInfo $"VpnWcfClient - serverAccessInfo: '%A{data.clientAccessInfo.serverAccessInfo}'"

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

            member _.sendPacket packet =
                Logger.logTrace (fun () -> $"sendPacket: Sending packet of size {packet.Length}, packet=%A{(summarizePacket packet)}")
                let payload = (data.clientAccessInfo.vpnClientId, packet)
                let result = tryCommunicate getService (fun s b -> s.sendPacket b) toSendError payload
                Logger.logTrace (fun () -> $"sendPacket: Received: '%A{result}'.")
                result

            member _.receivePackets clientId =
                Logger.logTrace (fun () -> $"receivePackets: Receiving packets for client {clientId.value}")
                let result = tryCommunicate getService (fun s b -> s.receivePackets b) toReceiveError clientId
                
                match result with
                | Ok (Some r) ->
                    r
                    |> Array.map (fun e -> Logger.logTrace (fun () -> $"Received for client {clientId.value}: '%A{(summarizePacket e)}'."))
                    |> ignore
                | Ok None -> Logger.logWarn $"ERROR: Empty response." 
                | Error e -> Logger.logWarn $"ERROR: '{e}'."    

                result


    let createVpnClient (clientAccessInfo: VpnClientAccessInfo) : IVpnClient =
        let data = { clientAccessInfo = clientAccessInfo }
        VpnWcfClient(data) :> IVpnClient
