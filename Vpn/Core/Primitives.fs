namespace Softellect.Vpn.Core

open System
open Softellect.Sys.Primitives
open Softellect.Sys.AppSettings

module Primitives =

    [<Literal>]
    let VpnWcfServiceName = "VpnWcfService"

    let adapterName = "SoftellectVPN"


    type VpnServerId =
        | VpnServerId of Guid

        member this.value = let (VpnServerId v) = this in v

        static member tryCreate (s: string) =
            match Guid.TryParse s with
            | true, g -> Some (VpnServerId g)
            | false, _ -> None

        static member create() = Guid.NewGuid() |> VpnServerId


    type VpnClientId =
        | VpnClientId of Guid

        member this.value = let (VpnClientId v) = this in v
        member this.configKey = $"{this.value:B}".ToUpper() |> ConfigKey

        static member tryCreate (s: string) =
            match Guid.TryParse s with
            | true, g -> Some (VpnClientId g)
            | false, _ -> None

        static member create() = Guid.NewGuid() |> VpnClientId


    type VpnClientName =
        | VpnClientName of string

        member this.value = let (VpnClientName v) = this in v


    type VpnSubnet =
        | VpnSubnet of string

        member this.value = let (VpnSubnet v) = this in v
        static member defaultValue = VpnSubnet "10.66.77.0/24"


    type VpnIpAddress =
        | VpnIpAddress of IpAddress

        member this.value = let (VpnIpAddress v) = this in v
        static member tryCreate (s: string) = IpAddress.tryCreate s |> Option.map VpnIpAddress


    let serverVpnIp = "10.66.77.1" |> Ip4 |> VpnIpAddress


    type LocalLanExclusion =
        | LocalLanExclusion of string

        member this.value = let (LocalLanExclusion v) = this in v

        static member defaultValues =
            [
                LocalLanExclusion "192.168.0.0/16"
                LocalLanExclusion "10.0.0.0/8"
                LocalLanExclusion "172.16.0.0/12"
                LocalLanExclusion "169.254.0.0/16"
                LocalLanExclusion "127.0.0.0/8"
            ]


    type VpnClientData =
        {
            clientName : VpnClientName
            assignedIp : VpnIpAddress
        }

        member data.serialize() =
            $"{nameof(data.clientName)}{ValueSeparator}{data.clientName.value}{ListSeparator}{nameof(data.assignedIp)}{ValueSeparator}{data.assignedIp.value.value}"


    type VpnClientConfig =
        {
            clientId : VpnClientId
            clientData : VpnClientData
        }


    type VpnPacket =
        {
            sourceIp : VpnIpAddress
            destinationIp : VpnIpAddress
            data : byte[]
        }


    type VpnAuthRequest =
        {
            clientId : VpnClientId
            timestamp : DateTime
            nonce : byte[]
        }


    type VpnAuthResponse =
        {
            assignedIp : VpnIpAddress
            serverPublicIp : VpnIpAddress
        }


    type VpnDataVersion =
        | VpnDataVersion of int

        member this.value = let (VpnDataVersion v) = this in v
        static member current = VpnDataVersion 1
