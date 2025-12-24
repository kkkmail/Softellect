namespace Softellect.Vpn.Server

open System
open CoreWCF
open Softellect.Sys.AppSettings
open Softellect.Sys.Core
open Softellect.Sys.Crypto
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Wcf.Common
open Softellect.Wcf.Service
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Wcf.Program
open Softellect.Wcf.Errors

module WcfServer =

    // let toAuthWcfError (e: WcfError) = e |> AuthWcfErr |> AuthWcfError |> AuthFailedErr |> ConnectionErr


    /// Try to load a client's public key by clientId.
    let tryLoadClientPublicKey (serverData: VpnServerData) (clientId: VpnClientId) =
        let keyId = KeyId clientId.value
        let keyFileName = FileName $"{clientId.value}.pkx"
        let keyFilePath = keyFileName.combine serverData.serverAccessInfo.clientKeysPath

        match tryImportPublicKey keyFilePath (Some keyId) with
        | Ok (_, publicKey) -> Some publicKey
        | Error e ->
            Logger.logWarn $"AuthWcfService: Failed to load public key for client {clientId.value}: '%A{e}'"
            None


    let inline private tryDecryptAndVerifyRequest<'T> (serverData: VpnServerData) (data : byte[]) verifier : Result<'T * PublicKey, VpnError> =
        Logger.logInfo $"Calling tryDecrypt with encryptionType: '%A{serverData.serverAccessInfo.encryptionType}'."

        match tryDecrypt serverData.serverAccessInfo.encryptionType data serverData.serverPrivateKey with
        | Ok r ->
            if r.Length < ClientIdPrefixSize then
                SnafyErr $"r.Length: {r.Length} is less than expected: {ClientIdPrefixSize}." |> Error
            else
                let clientIdBytes = r[0..ClientIdPrefixSize - 1]
                let clientId = Guid(clientIdBytes) |> VpnClientId
                Logger.logInfo $"Extracted clientId: '{clientId.value}' from clientIdBytes: '%A{clientIdBytes}'."

                match tryLoadClientPublicKey serverData clientId with
                | Some key ->
                    let encryptedPayload = Array.sub r ClientIdPrefixSize (r.Length - ClientIdPrefixSize)
                    match tryVerify encryptedPayload key with
                    | Ok verified ->
                        match tryDeserialize<'T> wcfSerializationFormat verified with
                        | Ok result ->
                            match verifier clientId result with
                            | true -> Ok (result, key)
                            | false -> SnafyErr $"Verification for client: '{clientId.value}' failed." |> Error
                        | Error e -> SnafyErr $"tryDeserialize for client: '{clientId.value}' failed, error: '%A{e}'." |> Error
                    | Error e -> SnafyErr $"tryVerify for client: '{clientId.value}' failed, error: '%A{e}'." |> Error
                | None -> SnafyErr $"tryLoadClientPublicKey for the client: '{clientId.value}' failed." |> Error
        | Error e -> SnafyErr $"tryDecrypt failed, error: '%A{e}'." |> Error


    let inline private trySignAndEncryptResponse (serverData: VpnServerData) clientKey response : Result<byte[], VpnError> =
        match trySerialize wcfSerializationFormat response with
        | Ok responseBytes ->
            match trySignAndEncrypt serverData.serverAccessInfo.encryptionType responseBytes serverData.serverPrivateKey clientKey with
            | Ok r -> Ok r
            | Error e -> SnafyErr $"trySignAndEncrypt failed, error: '%A{e}'." |> Error
        | Error e -> SnafyErr $"trySerialize failed, error: '%A{e}'." |> Error


    /// Encrypted auth service that wraps authentication with encryption/signing.
    /// Wire format for request: [clientId: 16 bytes][encrypted+signed VpnAuthRequest]
    /// Wire format for response: [encrypted+signed Result<VpnAuthResponse, VpnError>]
    [<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, IncludeExceptionDetailInFaults = true)>]
    type AuthWcfService (service: IAuthService, serverData: VpnServerData) =
        // let serverPrivateKey = serverData.serverPrivateKey
        // let clientKeysPath = serverData.serverAccessInfo.clientKeysPath
        //
        // /// Try to load a client's public key by clientId.
        // let tryLoadClientPublicKey (clientId: VpnClientId) =
        //     let keyId = KeyId clientId.value
        //     let keyFileName = FileName $"{clientId.value}.pkx"
        //     let keyFilePath = keyFileName.combine clientKeysPath
        //
        //     match tryImportPublicKey keyFilePath (Some keyId) with
        //     | Ok (_, publicKey) -> Some publicKey
        //     | Error e ->
        //         Logger.logWarn $"AuthWcfService: Failed to load public key for client {clientId.value}: '%A{e}'"
        //         None
        //
        // /// Process an encrypted WCF request and return an encrypted response.
        // /// Wire format: [clientId: 16 bytes][encrypted+signed payload]
        // let processEncryptedRequest<'TReq, 'TRes> (data: byte[]) (handler: 'TReq -> 'TRes) (opName: string) : byte[] =
        //     if data.Length < ClientIdPrefixSize then
        //         Logger.logError $"AuthWcfService.{opName}: Received data too short for clientId prefix"
        //         [||]
        //     else
        //         let clientIdBytes = data.[0..ClientIdPrefixSize - 1]
        //         let clientId = Guid(clientIdBytes) |> VpnClientId
        //         let encryptedPayload = Array.sub data ClientIdPrefixSize (data.Length - ClientIdPrefixSize)
        //
        //         Logger.logTrace (fun () -> $"AuthWcfService.{opName}: Processing request from client {clientId.value}")
        //
        //         match tryLoadClientPublicKey clientId with
        //         | Some clientPublicKey ->
        //             match tryDecryptAndVerify EncryptionType.AES encryptedPayload serverPrivateKey clientPublicKey with
        //             | Ok decryptedBytes ->
        //                 match tryDeserialize BinaryZippedFormat decryptedBytes with
        //                 | Ok (request: 'TReq) ->
        //                     let result = handler request
        //
        //                     match trySerialize BinaryZippedFormat result with
        //                     | Ok responseBytes ->
        //                         match tryEncryptAndSign EncryptionType.AES responseBytes serverPrivateKey clientPublicKey with
        //                         | Ok encryptedResponse ->
        //                             Logger.logTrace (fun () -> $"AuthWcfService.{opName}: Successfully processed for client {clientId.value}")
        //                             encryptedResponse
        //                         | Error e ->
        //                             Logger.logError $"AuthWcfService.{opName}: Failed to encrypt response for client {clientId.value}: '%A{e}'"
        //                             [||]
        //                     | Error e ->
        //                         Logger.logError $"AuthWcfService.{opName}: Failed to serialize response: '%A{e}'"
        //                         [||]
        //                 | Error e ->
        //                     Logger.logError $"AuthWcfService.{opName}: Failed to deserialize request from client {clientId.value}: '%A{e}'"
        //                     [||]
        //             | Error e ->
        //                 Logger.logError $"AuthWcfService.{opName}: Failed to decrypt/verify request from client {clientId.value}: '%A{e}'"
        //                 [||]
        //         | None ->
        //             Logger.logError $"AuthWcfService.{opName}: Unknown client {clientId.value} - no public key found"
        //             [||]

        let toAuthenticateError (e: WcfError) : VpnError = e |> AuthWcfErr |> VpnWcfErr |> VpnAuthErr |> VpnConnectionErr
        let toPingSessionError (e: WcfError) : VpnError = e |> PingWcfErr |> VpnWcfErr |> VpnAuthErr |> VpnConnectionErr

        let authenticateImpl data =
            let verifier c (r : VpnAuthRequest) = r.clientId = c

            match tryDecryptAndVerifyRequest<VpnAuthRequest> serverData data verifier with
            | Ok (r, k) ->
                let response = service.authenticate r

                match trySignAndEncryptResponse serverData k response with
                | Ok s -> Ok s
                | Error e -> SnafyErr $"trySignAndEncryptResponse failed, error: '%A{e}'." |> Error
            | Error e -> SnafyErr $"tryDecryptAndVerifyRequest failed, error: '%A{e}'." |> Error

        let pingSessionImpl data =
            let verifier c (r : VpnPingRequest) = r.clientId = c

            match tryDecryptAndVerifyRequest<VpnPingRequest> serverData data verifier with
            | Ok (r, k) ->
                let response = service.pingSession r

                match trySignAndEncryptResponse serverData k response with
                | Ok s -> Ok s
                | Error e -> SnafyErr $"trySignAndEncryptResponse failed, error: '%A{e}'." |> Error
            | Error e -> SnafyErr $"tryDecryptAndVerifyRequest failed, error: '%A{e}'." |> Error

        interface IAuthWcfService with
            member _.authenticate data = tryReply authenticateImpl toAuthenticateError data
            member _.pingSession data = tryReply pingSessionImpl toPingSessionError data


    /// AuthWcfService is injected into host first. Any additional services must be injected via configureServices.
    let getAuthWcfProgram (data : VpnServerData) getService argv configureServices =
        let postBuildHandler _ _ =
            Logger.logInfo $"vpnServerMain - VPN Server started with subnet: {data.serverAccessInfo.vpnSubnet.value}"

        let saveSettings() =
            let result = updateVpnServerAccessInfo data.serverAccessInfo
            Logger.logInfo $"saveSettings - result: '%A{result}'."

        let projectName = getProjectName() |> Some

        // Create a factory function that captures serverData
        let getWcfService (service: IAuthService) = AuthWcfService(service, data)

        let programData =
            {
                getService = fun () -> getService() :> IAuthService
                serviceAccessInfo = data.serverAccessInfo.serviceAccessInfo
                getWcfService = getWcfService
                saveSettings = saveSettings
                configureServices = configureServices
                configureServiceLogging = configureServiceLogging projectName
                configureLogging = configureLogging projectName
                postBuildHandler = Some postBuildHandler
            }

        fun () -> wcfMain<IAuthService, IAuthWcfService, AuthWcfService> ProgramName programData argv
