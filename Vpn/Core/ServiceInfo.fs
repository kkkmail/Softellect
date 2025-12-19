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


    type IVpnClient =
        abstract authenticate : VpnAuthRequest -> VpnAuthResult
        abstract sendPackets : byte[][] -> VpnUnitResult
        abstract receivePackets : VpnClientId -> VpnPacketsResult


    type IVpnService =
        inherit IHostedService
        abstract authenticate : VpnAuthRequest -> VpnAuthResult
        abstract sendPackets : VpnClientId * byte[][] -> VpnUnitResult
        abstract receivePackets : VpnClientId -> VpnPacketsResult


    [<ServiceContract(ConfigurationName = VpnWcfServiceName)>]
    type IVpnWcfService =

        [<OperationContract(Name = "authenticate")>]
        abstract authenticate : data:byte[] -> byte[]

        [<OperationContract(Name = "sendPackets")>]
        abstract sendPackets : data:byte[] -> byte[]

        [<OperationContract(Name = "receivePackets")>]
        abstract receivePackets : data:byte[] -> byte[]


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
        }


    type VpnServerData =
        {
            serverAccessInfo : VpnServerAccessInfo
            serverPrivateKey : PrivateKey
            serverPublicKey : PublicKey
        }
