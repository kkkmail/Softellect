namespace Softellect.Vpn.Server

open CoreWCF
open Softellect.Sys.AppSettings
open Softellect.Sys.Logging
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Wcf.Service
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Wcf.Program
open Softellect.Wcf.Errors

module WcfServer =

    let toAuthWcfError (e: WcfError) = e |> AuthWcfErr |> AuthWcfError |> AuthFailedErr |> ConnectionErr


    [<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, IncludeExceptionDetailInFaults = true)>]
    type AuthWcfService(service: IAuthService) =
        interface IAuthWcfService with
            member _.authenticate data =
                tryReply service.authenticate toAuthWcfError data


    /// AuthWcfService is injected into host first. Any additional services must be injected via configureServices.
    let getAuthWcfProgram (data : VpnServerData) getService argv configureServices =
        let postBuildHandler _ _ =
            Logger.logInfo $"vpnServerMain - VPN Server started with subnet: {data.serverAccessInfo.vpnSubnet.value}"

        let saveSettings() =
            let result = updateVpnServerAccessInfo data.serverAccessInfo
            Logger.logInfo $"saveSettings - result: '%A{result}'."

        let projectName = getProjectName() |> Some

        let programData =
            {
                getService = fun () -> getService() :> IAuthService
                serviceAccessInfo = data.serverAccessInfo.serviceAccessInfo
                getWcfService = AuthWcfService
                saveSettings = saveSettings
                configureServices = configureServices
                configureServiceLogging = configureServiceLogging projectName
                configureLogging = configureLogging projectName
                postBuildHandler = Some postBuildHandler
            }

        fun () -> wcfMain<IAuthService, IAuthWcfService, AuthWcfService> ProgramName programData argv
