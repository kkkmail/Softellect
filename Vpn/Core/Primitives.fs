namespace Softellect.Vpn.Core

open System
open System.Net
open Softellect.Sys.Primitives

module Primitives =

    [<Literal>]
    let VpnWcfServiceName = "VpnWcfService"


    type VpnClientId =
        | VpnClientId of Guid

        member this.value = let (VpnClientId v) = this in v
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
        | VpnIpAddress of IPAddress

        member this.value = let (VpnIpAddress v) = this in v

        static member tryParse (s: string) =
            match IPAddress.TryParse s with
            | true, ip -> Some (VpnIpAddress ip)
            | false, _ -> None


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


    type VpnClientConfig =
        {
            clientId : VpnClientId
            clientName : VpnClientName
            assignedIp : VpnIpAddress
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
            success : bool
            assignedIp : VpnIpAddress option
            serverPublicIp : VpnIpAddress option
            errorMessage : string option
        }


    type VpnDataVersion =
        | VpnDataVersion of int

        member this.value = let (VpnDataVersion v) = this in v
        static member current = VpnDataVersion 1
