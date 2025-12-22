namespace Softellect.Vpn.Client

open System
open Softellect.Sys.Core
open Softellect.Sys.Crypto
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Wcf.Client
open Softellect.Wcf.Errors
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo

module WcfClient =

    /// Size of clientId prefix in encrypted auth messages.
    [<Literal>]
    let ClientIdPrefixSize = 16


    /// Encrypted auth WCF client.
    /// Wire format for request: [clientId: 16 bytes][encrypted+signed VpnAuthRequest]
    /// Wire format for response: [encrypted+signed Result<VpnAuthResponse, VpnError>]
    type AuthWcfClient(data: VpnClientServiceData) =
        let clientAccessInfo = data.clientAccessInfo
        let toAuthError (e: WcfError) = e |> AuthWcfErr |> AuthWcfError |> AuthFailedErr |> ConnectionErr
        let url = clientAccessInfo.serverAccessInfo.getUrl()
        let commType = clientAccessInfo.serverAccessInfo.communicationType
        let clientPrivateKey = data.clientPrivateKey
        let serverPublicKey = data.serverPublicKey
        let vpnClientId = clientAccessInfo.vpnClientId

        do Logger.logInfo $"AuthWcfClient created - URL: '{url}', CommType: '%A{commType}'"
        do Logger.logInfo $"AuthWcfClient - serverAccessInfo: '%A{clientAccessInfo.serverAccessInfo}'"

        let getService() =
            Logger.logTrace (fun () -> $"AuthWcfClient.getService - About to call tryGetWcfService with URL: '{url}', CommType: '%A{commType}'")
            let result = tryGetWcfService<IAuthWcfService> commType url
            match result with
            | Ok _ -> Logger.logTrace (fun () -> "AuthWcfClient.getService - Successfully created WCF service proxy")
            | Error e -> Logger.logError $"AuthWcfClient.getService - Failed to create WCF service proxy: %A{e}"
            result

        interface IAuthClient with
            member _.authenticate request =
                Logger.logTrace (fun () -> $"authenticate: Sending encrypted auth request for client {request.clientId.value}")

                // Serialize the request
                match trySerialize BinaryZippedFormat request with
                | Ok requestBytes ->
                    // Encrypt and sign the request
                    match tryEncryptAndSign EncryptionType.AES requestBytes clientPrivateKey serverPublicKey with
                    | Ok encryptedRequest ->
                        // Build wire format: [clientId: 16 bytes][encrypted payload]
                        let clientIdBytes = vpnClientId.value.ToByteArray()
                        let wireData = Array.append clientIdBytes encryptedRequest

                        // Send to server
                        match getService() with
                        | Ok (service, factoryCloser) ->
                            try
                                let encryptedResponse = service.authenticate wireData
                                (service :?> System.ServiceModel.IClientChannel).Close()
                                factoryCloser()

                                if encryptedResponse.Length = 0 then
                                    Logger.logError "AuthWcfClient: Received empty response from server"
                                    Error (AuthCryptoErr |> AuthFailedErr |> ConnectionErr)
                                else
                                    // Decrypt and verify the response
                                    match tryDecryptAndVerify EncryptionType.AES encryptedResponse clientPrivateKey serverPublicKey with
                                    | Ok decryptedBytes ->
                                        // Deserialize the response
                                        match tryDeserialize<Result<VpnAuthResponse, VpnError>> BinaryZippedFormat decryptedBytes with
                                        | Ok result ->
                                            Logger.logTrace (fun () -> $"AuthWcfClient: Successfully received auth response")
                                            result
                                        | Error e ->
                                            Logger.logError $"AuthWcfClient: Failed to deserialize response: '%A{e}'"
                                            Error (AuthCryptoErr |> AuthFailedErr |> ConnectionErr)
                                    | Error e ->
                                        Logger.logError $"AuthWcfClient: Failed to decrypt/verify response: '%A{e}'"
                                        Error (AuthCryptoErr |> AuthFailedErr |> ConnectionErr)
                            with
                            | ex ->
                                Logger.logError $"AuthWcfClient: WCF call failed: {ex.Message}"
                                try
                                    (service :?> System.ServiceModel.IClientChannel).Abort()
                                    factoryCloser()
                                with _ -> ()
                                Error (ex |> WcfExn |> toAuthError)
                        | Error e ->
                            Logger.logError $"AuthWcfClient: Failed to get service: '%A{e}'"
                            Error (e |> toAuthError)
                    | Error e ->
                        Logger.logError $"AuthWcfClient: Failed to encrypt/sign request: '%A{e}'"
                        Error (AuthCryptoErr |> AuthFailedErr |> ConnectionErr)
                | Error e ->
                    Logger.logError $"AuthWcfClient: Failed to serialize request: '%A{e}'"
                    Error (AuthCryptoErr |> AuthFailedErr |> ConnectionErr)


    let createAuthWcfClient (serviceData: VpnClientServiceData) : IAuthClient =
        AuthWcfClient(serviceData) :> IAuthClient
