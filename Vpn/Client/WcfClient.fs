namespace Softellect.Vpn.Client

open Softellect.Sys.Core
open Softellect.Sys.Crypto
open Softellect.Sys.Logging
open Softellect.Wcf.Client
open Softellect.Wcf.Common
open Softellect.Wcf.Errors
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo

module WcfClient =

    let inline private trySignAndEncryptRequest (data: VpnClientServiceData) request =
        match trySerialize wcfSerializationFormat request with
        | Ok requestBytes ->
            let clientIdBytes = data.clientAccessInfo.vpnClientId.value.ToByteArray()
            Logger.logTrace (fun () -> $"Obtained clientIdBytes: '%A{clientIdBytes}' for client: '{data.clientAccessInfo.vpnClientId.value}'.")

            // Append VpnClientId bytes BEFORE the serialized payload.
            let toBeEncryptedData = Array.append clientIdBytes requestBytes

            Logger.logTrace (fun () -> $"Calling trySignAndEncrypt with encryptionType: '%A{data.clientAccessInfo.encryptionType}' for client: '{data.clientAccessInfo.vpnClientId.value}'.")
            match trySignAndEncrypt data.clientAccessInfo.encryptionType toBeEncryptedData data.clientPrivateKey data.serverPublicKey with
            | Ok r -> Ok r
            | Error e -> e |> SysErr |> VpnAuthErr |> VpnConnectionErr |> Error
        | Error e -> e |> SerializationErr |> VpnAuthErr |> VpnConnectionErr |> Error


    let inline private tryDecryptAndVerifyResponse<'T> (data: VpnClientServiceData) response =
        match tryDecryptAndVerify data.clientAccessInfo.encryptionType response data.clientPrivateKey data.serverPublicKey with
        | Ok verified ->
            match tryDeserialize<'T> wcfSerializationFormat verified with
            | Ok result -> Ok result
            | Error e -> SnafyErr $"tryDeserialize failed, error: '%A{e}'." |> Error
        | Error e -> SnafyErr $"tryDecryptAndVerify failed, error: '%A{e}'." |> Error


    /// Encrypted auth WCF client.
    type AuthWcfClient (data: VpnClientServiceData) =
        let clientAccessInfo = data.clientAccessInfo
        let url = clientAccessInfo.serverAccessInfo.getUrl()
        let commType = clientAccessInfo.serverAccessInfo.communicationType

        do Logger.logTrace (fun () -> $"AuthWcfClient created - URL: '{url}', CommType: '%A{commType}'")
        do Logger.logTrace (fun () -> $"AuthWcfClient - serverAccessInfo: '%A{clientAccessInfo.serverAccessInfo}'")

        let tryGetWcfService() = tryGetWcfService<IAuthWcfService> commType url
        let toAuthError (e: WcfError) = e |> AuthWcfErr |> VpnWcfErr |> VpnAuthErr |> VpnConnectionErr
        let toPingError (e: WcfError) = e |> PingWcfErr |> VpnWcfErr |> VpnAuthErr |> VpnConnectionErr

        let authenticateImpl (request : VpnAuthRequest) =
            match trySignAndEncryptRequest data request with
            | Ok r ->
                  tryCommunicate tryGetWcfService (fun service -> service.authenticate) toAuthError r
                  |> Result.bind (tryDecryptAndVerifyResponse data)
            | Error e -> Error e

        let pingSessionImpl (request : VpnPingRequest) =
            match trySignAndEncryptRequest data request with
            | Ok r ->
                tryCommunicate tryGetWcfService (fun service -> service.pingSession) toPingError r
                |> Result.bind (tryDecryptAndVerifyResponse data)
            | Error e -> Error e

        interface IAuthClient with
            member _.authenticate request = authenticateImpl request
            member _.pingSession request =  pingSessionImpl request


    let createAuthWcfClient (serviceData: VpnClientServiceData) : IAuthClient =
        AuthWcfClient(serviceData) :> IAuthClient
