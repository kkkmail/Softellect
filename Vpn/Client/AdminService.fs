namespace Softellect.Vpn.Client

open CoreWCF
open Softellect.Sys.Logging
open Softellect.Wcf.Client
open Softellect.Wcf.Common
open Softellect.Wcf.Service
open Softellect.Wcf.Errors
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo

module AdminService =

    /// Error mapping for admin WCF operations.
    let private toAdminWcfError (e: WcfError) : VpnError =
        e |> VpnWcfError.AdminWcfErr |> VpnAdminError.AdminWcfErr |> VpnAdminErr


    /// Admin WCF service implementation.
    /// Wraps high-level IAdminService with byte[] serialization for WCF transport.
    [<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, IncludeExceptionDetailInFaults = true)>]
    type AdminWcfService(service: IAdminService) =

        let getStatusImpl () =
            Logger.logTrace (fun () -> "AdminWcfService.getStatus called.")
            let status = service.getStatus()
            Logger.logTrace (fun () -> $"AdminWcfService.getStatus returning: '%A{status}'.")
            Ok status

        let startVpnImpl () =
            Logger.logInfo "AdminWcfService.startVpn called."
            service.startVpn()

        let stopVpnImpl () =
            Logger.logInfo "AdminWcfService.stopVpn called."
            service.stopVpn()

        interface IAdminWcfService with
            member _.getStatus data = tryReply getStatusImpl toAdminWcfError data
            member _.startVpn data = tryReply startVpnImpl toAdminWcfError data
            member _.stopVpn data = tryReply stopVpnImpl toAdminWcfError data


    /// Admin WCF client for tray UI to communicate with the service.
    type AdminWcfClient(serviceAccessInfo: ServiceAccessInfo) =
        let url = serviceAccessInfo.getUrl()
        let commType = serviceAccessInfo.communicationType

        do Logger.logTrace (fun () -> $"AdminWcfClient created - URL: '{url}', CommType: '%A{commType}'")

        let tryGetWcfService() = tryGetWcfService<IAdminWcfService> commType url
        let toError (e: WcfError) = e |> VpnWcfError.AdminWcfErr |> VpnAdminError.AdminWcfErr |> VpnAdminErr

        let getStatusImpl () =
            tryCommunicate tryGetWcfService (fun service -> service.getStatus) toError ()

        let startVpnImpl () =
            tryCommunicate tryGetWcfService (fun service -> service.startVpn) toError ()

        let stopVpnImpl () =
            tryCommunicate tryGetWcfService (fun service -> service.stopVpn) toError ()

        /// Gets the current VPN connection status from the service.
        member _.getStatus() : Result<VpnClientConnectionState, VpnError> = getStatusImpl()

        /// Starts the VPN connection via the service.
        member _.startVpn() : AdminUnitResult = startVpnImpl()

        /// Stops the VPN connection via the service.
        member _.stopVpn() : AdminUnitResult = stopVpnImpl()


    /// Creates an admin WCF client using the provided service access info.
    let createAdminWcfClient (serviceAccessInfo: ServiceAccessInfo) =
        AdminWcfClient(serviceAccessInfo)
