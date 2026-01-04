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


    type VpnClientAccessInfo =
        {
            vpnClientId : VpnClientId
            vpnServerId : VpnServerId
            serverAccessInfo : ServiceAccessInfo
            clientKeyPath : FolderName
            serverPublicKeyPath : FolderName
            localLanExclusions : LocalLanExclusion list
            vpnTransportProtocol : VpnTransportProtocol
            physicalGatewayIp : IpAddress
            physicalInterfaceName : string
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
