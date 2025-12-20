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
    type VpnWcfService(service: IVpnService) =

        let toSendWcfError (e: WcfError) = e |> SendPacketWcfErr |> fun _ -> ConfigErr "Send error"
        let toReceiveWcfError (e: WcfError) = e |> ReceivePacketsWcfErr |> fun _ -> ConfigErr "Receive error"

        interface IVpnWcfService with
            member _.authenticate data =
                tryReply service.authenticate toAuthWcfError data

            member _.sendPackets data =
                tryReply service.sendPackets toSendWcfError data

            member _.receivePackets data =
                tryReply service.receivePackets toReceiveWcfError data


    let getWcfProgram (data : VpnServerData) getService argv =
        let postBuildHandler _ _ =
            Logger.logInfo $"vpnServerMain - VPN Server started with subnet: {data.serverAccessInfo.vpnSubnet.value}"

        let saveSettings() =
            let result = updateVpnServerAccessInfo data.serverAccessInfo
            Logger.logInfo $"saveSettings - result: '%A{result}'."

        let projectName = getProjectName() |> Some

        let programData =
            {
                getService = getService
                serviceAccessInfo = data.serverAccessInfo.serviceAccessInfo
                getWcfService = VpnWcfService
                saveSettings = saveSettings
                configureServices = None
                configureServiceLogging = configureServiceLogging projectName
                configureLogging = configureLogging projectName
                postBuildHandler = Some postBuildHandler
            }

        fun () -> wcfMain<IVpnService, IVpnWcfService, VpnWcfService> ProgramName programData argv


    [<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, IncludeExceptionDetailInFaults = true)>]
    type AuthWcfService(service: IAuthService) =
        interface IAuthWcfService with
            member _.authenticate data =
                tryReply service.authenticate toAuthWcfError data


    /// TODO kk:20251220 - OK, this is ugly. Let's see if it works and if it does, then make it less ugly
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
