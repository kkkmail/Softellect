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
open Softellect.Wcf.Service
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Wcf.Program
open Softellect.Wcf.Errors

module WcfServer =

    let toAuthWcfError (e: WcfError) = e |> AuthWcfErr |> AuthWcfError |> AuthFailedErr |> ConnectionErr

    /// Size of clientId prefix in encrypted auth messages.
    [<Literal>]
    let ClientIdPrefixSize = 16


    /// Encrypted auth service that wraps authentication with encryption/signing.
    /// Wire format for request: [clientId: 16 bytes][encrypted+signed VpnAuthRequest]
    /// Wire format for response: [encrypted+signed Result<VpnAuthResponse, VpnError>]
    [<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, IncludeExceptionDetailInFaults = true)>]
    type AuthWcfService(service: IAuthService, serverData: VpnServerData) =
        let serverPrivateKey = serverData.serverPrivateKey
        let clientKeysPath = serverData.serverAccessInfo.clientKeysPath

        /// Try to load a client's public key by clientId.
        let tryLoadClientPublicKey (clientId: VpnClientId) =
            let keyId = KeyId clientId.value
            let keyFileName = FileName $"{clientId.value}.pkx"
            let keyFilePath = keyFileName.combine clientKeysPath

            match tryImportPublicKey keyFilePath (Some keyId) with
            | Ok (_, publicKey) -> Some publicKey
            | Error e ->
                Logger.logWarn $"AuthWcfService: Failed to load public key for client {clientId.value}: '%A{e}'"
                None

        interface IAuthWcfService with
            member _.authenticate data =
                // Wire format: [clientId: 16 bytes][encrypted+signed payload]
                if data.Length < ClientIdPrefixSize then
                    Logger.logError "AuthWcfService: Received data too short for clientId prefix"
                    [||]
                else
                    // Extract clientId from unencrypted prefix
                    let clientIdBytes = data.[0..ClientIdPrefixSize - 1]
                    let clientId = Guid(clientIdBytes) |> VpnClientId
                    let encryptedPayload = Array.sub data ClientIdPrefixSize (data.Length - ClientIdPrefixSize)

                    Logger.logTrace (fun () -> $"AuthWcfService: Processing auth request from client {clientId.value}")

                    match tryLoadClientPublicKey clientId with
                    | Some clientPublicKey ->
                        // Decrypt and verify the request
                        match tryDecryptAndVerify EncryptionType.AES encryptedPayload serverPrivateKey clientPublicKey with
                        | Ok decryptedBytes ->
                            // Deserialize the request
                            match tryDeserialize BinaryZippedFormat decryptedBytes with
                            | Ok (request: VpnAuthRequest) ->
                                // Process the authentication
                                let result = service.authenticate request

                                // Serialize the response
                                match trySerialize BinaryZippedFormat result with
                                | Ok responseBytes ->
                                    // Encrypt and sign the response
                                    match tryEncryptAndSign EncryptionType.AES responseBytes serverPrivateKey clientPublicKey with
                                    | Ok encryptedResponse ->
                                        Logger.logTrace (fun () -> $"AuthWcfService: Successfully processed auth for client {clientId.value}")
                                        encryptedResponse
                                    | Error e ->
                                        Logger.logError $"AuthWcfService: Failed to encrypt response for client {clientId.value}: '%A{e}'"
                                        [||]
                                | Error e ->
                                    Logger.logError $"AuthWcfService: Failed to serialize response: '%A{e}'"
                                    [||]
                            | Error e ->
                                Logger.logError $"AuthWcfService: Failed to deserialize request from client {clientId.value}: '%A{e}'"
                                [||]
                        | Error e ->
                            Logger.logError $"AuthWcfService: Failed to decrypt/verify request from client {clientId.value}: '%A{e}'"
                            [||]
                    | None ->
                        Logger.logError $"AuthWcfService: Unknown client {clientId.value} - no public key found"
                        [||]


    /// AuthWcfService is injected into host first. Any additional services must be injected via configureServices.
    let getAuthWcfProgram (data : VpnServerData) getService argv configureServices =
        let postBuildHandler _ _ =
            Logger.logInfo $"vpnServerMain - VPN Server started with subnet: {data.serverAccessInfo.vpnSubnet.value}"

        let saveSettings() =
            let result = updateVpnServerAccessInfo data.serverAccessInfo
            Logger.logInfo $"saveSettings - result: '%A{result}'."

        let projectName = getProjectName() |> Some

        // Create a factory function that captures serverData
        let createWcfService (service: IAuthService) = AuthWcfService(service, data)

        let programData =
            {
                getService = fun () -> getService() :> IAuthService
                serviceAccessInfo = data.serverAccessInfo.serviceAccessInfo
                getWcfService = createWcfService
                saveSettings = saveSettings
                configureServices = configureServices
                configureServiceLogging = configureServiceLogging projectName
                configureLogging = configureLogging projectName
                postBuildHandler = Some postBuildHandler
            }

        fun () -> wcfMain<IAuthService, IAuthWcfService, AuthWcfService> ProgramName programData argv
