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
            Logger.logInfo $"Obtained clientIdBytes: '%A{clientIdBytes}' for client: '{data.clientAccessInfo.vpnClientId.value}'."

            // Append VpnClientId bytes BEFORE the serialized payload.
            let toBeEncryptedData = Array.append clientIdBytes requestBytes

            Logger.logInfo $"Calling trySignAndEncrypt with encryptionType: '%A{data.clientAccessInfo.encryptionType}' for client: '{data.clientAccessInfo.vpnClientId.value}'."
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
    /// Wire format for request: [clientId: 16 bytes][encrypted+signed VpnAuthRequest]
    /// Wire format for response: [encrypted+signed Result<VpnAuthResponse, VpnError>]
    type AuthWcfClient (data: VpnClientServiceData) =
        let clientAccessInfo = data.clientAccessInfo
        let url = clientAccessInfo.serverAccessInfo.getUrl()
        let commType = clientAccessInfo.serverAccessInfo.communicationType
        // let clientPrivateKey = data.clientPrivateKey
        // let serverPublicKey = data.serverPublicKey
        // let vpnClientId = clientAccessInfo.vpnClientId

        do Logger.logInfo $"AuthWcfClient created - URL: '{url}', CommType: '%A{commType}'"
        do Logger.logInfo $"AuthWcfClient - serverAccessInfo: '%A{clientAccessInfo.serverAccessInfo}'"

        // let getService() =
        //     Logger.logTrace (fun () -> $"AuthWcfClient.getService - About to call tryGetWcfService with URL: '{url}', CommType: '%A{commType}'")
        //     let result = tryGetWcfService<IAuthWcfService> commType url
        //     match result with
        //     | Ok _ -> Logger.logTrace (fun () -> "AuthWcfClient.getService - Successfully created WCF service proxy")
        //     | Error e -> Logger.logError $"AuthWcfClient.getService - Failed to create WCF service proxy: %A{e}"
        //     result
        //
        // /// Send an encrypted WCF request and decrypt the response.
        // let sendEncryptedRequest<'TReq, 'TRes> (request: 'TReq) (wcfCall: IAuthWcfService -> byte[] -> byte[]) (opName: string) : Result<'TRes, VpnError> =
        //     Logger.logTrace (fun () -> $"AuthWcfClient.{opName}: Sending encrypted request")
        //
        //     match trySerialize BinaryZippedFormat request with
        //     | Ok requestBytes ->
        //         match tryEncryptAndSign EncryptionType.AES requestBytes clientPrivateKey serverPublicKey with
        //         | Ok encryptedRequest ->
        //             let clientIdBytes = vpnClientId.value.ToByteArray()
        //             let wireData = Array.append clientIdBytes encryptedRequest
        //
        //             match getService() with
        //             | Ok (service, factoryCloser) ->
        //                 try
        //                     let encryptedResponse = wcfCall service wireData
        //                     (service :?> System.ServiceModel.IClientChannel).Close()
        //                     factoryCloser()
        //
        //                     if encryptedResponse.Length = 0 then
        //                         Logger.logError $"AuthWcfClient.{opName}: Received empty response from server"
        //                         Error (AuthCryptoErr |> AuthFailedErr |> ConnectionErr)
        //                     else
        //                         match tryDecryptAndVerify EncryptionType.AES encryptedResponse clientPrivateKey serverPublicKey with
        //                         | Ok decryptedBytes ->
        //                             match tryDeserialize<'TRes> BinaryZippedFormat decryptedBytes with
        //                             | Ok result ->
        //                                 Logger.logTrace (fun () -> $"AuthWcfClient.{opName}: Successfully received response")
        //                                 Ok result
        //                             | Error e ->
        //                                 Logger.logError $"AuthWcfClient.{opName}: Failed to deserialize response: '%A{e}'"
        //                                 Error (AuthCryptoErr |> AuthFailedErr |> ConnectionErr)
        //                         | Error e ->
        //                             Logger.logError $"AuthWcfClient.{opName}: Failed to decrypt/verify response: '%A{e}'"
        //                             Error (AuthCryptoErr |> AuthFailedErr |> ConnectionErr)
        //                 with
        //                 | ex ->
        //                     Logger.logError $"AuthWcfClient.{opName}: WCF call failed: {ex.Message}"
        //                     try
        //                         (service :?> System.ServiceModel.IClientChannel).Abort()
        //                         factoryCloser()
        //                     with _ -> ()
        //                     Error (ex |> WcfExn |> toAuthError)
        //             | Error e ->
        //                 Logger.logError $"AuthWcfClient.{opName}: Failed to get service: '%A{e}'"
        //                 Error (e |> toAuthError)
        //         | Error e ->
        //             Logger.logError $"AuthWcfClient.{opName}: Failed to encrypt/sign request: '%A{e}'"
        //             Error (AuthCryptoErr |> AuthFailedErr |> ConnectionErr)
        //     | Error e ->
        //         Logger.logError $"AuthWcfClient.{opName}: Failed to serialize request: '%A{e}'"
        //         Error (AuthCryptoErr |> AuthFailedErr |> ConnectionErr)

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
