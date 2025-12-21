namespace Softellect.Vpn.Client

open Softellect.Sys.Logging
open Softellect.Wcf.Client
open Softellect.Wcf.Errors
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo

module WcfClient =

    type AuthWcfClient(data: VpnClientAccessInfo) =
        let toAuthError (e: WcfError) = e |> AuthWcfErr |> AuthWcfError |> AuthFailedErr |> ConnectionErr
        let url = data.serverAccessInfo.getUrl()
        let commType = data.serverAccessInfo.communicationType

        do Logger.logInfo $"AuthWcfClient created - URL: '{url}', CommType: '%A{commType}'"
        do Logger.logInfo $"AuthWcfClient - serverAccessInfo: '%A{data.serverAccessInfo}'"

        let getService() =
            Logger.logTrace (fun () -> $"AuthWcfClient.getService - About to call tryGetWcfService with URL: '{url}', CommType: '%A{commType}'")
            let result = tryGetWcfService<IAuthWcfService> commType url
            match result with
            | Ok _ -> Logger.logTrace (fun () -> "AuthWcfClient.getService - Successfully created WCF service proxy")
            | Error e -> Logger.logError $"AuthWcfClient.getService - Failed to create WCF service proxy: %A{e}"
            result

        interface IAuthClient with
            member _.authenticate request =
                Logger.logTrace (fun () -> $"authenticate: Sending auth request for client {request.clientId.value}")
                tryCommunicate getService (fun s b -> s.authenticate b) toAuthError request


    let createAuthWcfClient (clientAccessInfo: VpnClientAccessInfo) : IAuthClient =
        AuthWcfClient(clientAccessInfo) :> IAuthClient
