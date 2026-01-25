namespace Softellect.Vpn.Core

open System.ServiceModel
#if !ANDROID
open Microsoft.Extensions.Hosting
#endif
open Softellect.Sys.Primitives
open Softellect.Wcf.Common
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors

module ServiceInfo =

    type VpnPacketResult = Result<byte[] option, VpnError>
    type VpnPacketsResult = Result<byte[][] option, VpnError>
    type VpnAuthResult = Result<VpnAuthResponse, VpnError>


    type VpnPingResult = Result<unit, VpnError>
    type VpnVersionInfoResult = Result<VpnVersionInfoResponse, VpnError>


    type IAuthClient =
        abstract getVersionInfo : unit -> VpnVersionInfoResult
        abstract authenticate : VpnAuthRequest -> VpnAuthResult
        abstract pingSession : VpnPingRequest -> VpnPingResult

#if !ANDROID
    type IAuthService =
        inherit IHostedService
        abstract getVersionInfo : unit -> VpnVersionInfoResult
        abstract authenticate : VpnAuthRequest -> VpnAuthResult
        abstract pingSession : VpnPingRequest -> VpnPingResult


    type IVpnPushService =
        inherit IHostedService
        abstract sendPackets : VpnSessionId * byte[][] -> VpnUnitResult
        abstract receivePackets : VpnSessionId -> VpnPacketsResult
#endif

    [<ServiceContract(ConfigurationName = AuthServiceName)>]
    type IAuthWcfService =

        /// Spec 057: Version handshake - stable contract, do not modify.
        [<OperationContract(Name = "getVersionInfo")>]
        abstract getVersionInfo : data:byte[] -> byte[]

        [<OperationContract(Name = "authenticate")>]
        abstract authenticate : data:byte[] -> byte[]

        [<OperationContract(Name = "pingSession")>]
        abstract pingSession : data:byte[] -> byte[]


    type VpnServerAccessInfo =
        {
            vpnDataVersion : VpnDataVersion
            vpnServerId : VpnServerId
            serviceAccessInfo : ServiceAccessInfo
            vpnSubnet : VpnSubnet
            serverKeyPath : FolderName
            clientKeysPath : FolderName
            vpnTransportProtocol : VpnTransportProtocol
            encryptionType : EncryptionType
        }


    type VpnConnectionInfo =
        {
            vpnConnectionName : VpnConnectionName
            serverAccessInfo : ServiceAccessInfo
        }


    type VpnClientAccessInfo =
        {
            vpnClientId : VpnClientId
            vpnServerId : VpnServerId
            vpnConnectionInfo : VpnConnectionInfo
            vpnConnections : VpnConnectionInfo list
            clientKeyPath : FolderName
            serverPublicKeyPath : FolderName
            localLanExclusions : LocalLanExclusion list
            vpnTransportProtocol : VpnTransportProtocol
            physicalGatewayInfo : PhysicalGatewayInfo
            useEncryption : bool
            encryptionType : EncryptionType
        }


    type VpnServerData =
        {
            serverAccessInfo : VpnServerAccessInfo
            serverPrivateKey : PrivateKey
            serverPublicKey : PublicKey
        }


    type VpnClientServiceData =
        {
            clientAccessInfo : VpnClientAccessInfo
            clientPrivateKey : PrivateKey
            clientPublicKey : PublicKey
            serverPublicKey : PublicKey
        }


#if !ANDROID
    /// VPN connection state for admin interface and UI display.
    type VpnClientConnectionState =
        | Disconnected
        | Connecting
        | Connected of VpnIpAddress
        | Reconnecting
        | Failed of string
        | VersionError of string


    /// Admin service result types.
    type AdminUnitResult = Result<unit, VpnError>


    /// High-level F# interface for admin service.
    /// Used by the service to expose VPN control operations.
    type IAdminService =
        inherit IHostedService
        abstract getStatus : unit -> VpnClientConnectionState
        abstract startVpn : unit -> AdminUnitResult
        abstract stopVpn : unit -> AdminUnitResult


    /// Low-level WCF interface for admin service.
    /// All methods use byte[] -> byte[] pattern for WCF serialization.
    [<ServiceContract(ConfigurationName = "VpnAdminService")>]
    type IAdminWcfService =

        [<OperationContract(Name = "getStatus")>]
        abstract getStatus : data:byte[] -> byte[]

        [<OperationContract(Name = "startVpn")>]
        abstract startVpn : data:byte[] -> byte[]

        [<OperationContract(Name = "stopVpn")>]
        abstract stopVpn : data:byte[] -> byte[]
#endif
