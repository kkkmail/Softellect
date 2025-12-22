namespace Softellect.Vpn.Core

open System.ServiceModel
open Microsoft.Extensions.Hosting
open Softellect.Sys.Primitives
open Softellect.Wcf.Common
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors

module ServiceInfo =

    type VpnPacketResult = Result<byte[] option, VpnError>
    type VpnPacketsResult = Result<byte[][] option, VpnError>
    type VpnAuthResult = Result<VpnAuthResponse, VpnError>


    type IAuthClient =
        abstract authenticate : VpnAuthRequest -> VpnAuthResult


    type IAuthService =
        inherit IHostedService
        abstract authenticate : VpnAuthRequest -> VpnAuthResult


    type IVpnPushService =
        inherit IHostedService
        abstract sendPackets : VpnClientId * byte[][] -> VpnUnitResult
        abstract receivePackets : VpnClientId -> VpnPacketsResult


    [<ServiceContract(ConfigurationName = AuthServiceName)>]
    type IAuthWcfService =

        [<OperationContract(Name = "authenticate")>]
        abstract authenticate : data:byte[] -> byte[]


    type VpnServerAccessInfo =
        {
            vpnDataVersion : VpnDataVersion
            vpnServerId : VpnServerId
            serviceAccessInfo : ServiceAccessInfo
            vpnSubnet : VpnSubnet
            serverKeyPath : FolderName
            clientKeysPath : FolderName
            vpnTransportProtocol : VpnTransportProtocol
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
        }


    type VpnServerData =
        {
            serverAccessInfo : VpnServerAccessInfo
            serverPrivateKey : PrivateKey
            serverPublicKey : PublicKey
        }
