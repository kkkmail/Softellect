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

module WcfServer =

    [<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, IncludeExceptionDetailInFaults = true)>]
    type VpnWcfService(service: IVpnService) =

        let toAuthWcfError (e: Softellect.Wcf.Errors.WcfError) = e |> AuthWcfErr |> AuthWcfError |> AuthFailedErr |> ConnectionErr
        let toSendWcfError (e: Softellect.Wcf.Errors.WcfError) = e |> SendPacketWcfErr |> fun _ -> ConfigErr "Send error"
        let toReceiveWcfError (e: Softellect.Wcf.Errors.WcfError) = e |> ReceivePacketsWcfErr |> fun _ -> ConfigErr "Receive error"

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
