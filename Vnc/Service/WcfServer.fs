namespace Softellect.Vnc.Service

open System
open CoreWCF
open Softellect.Sys.AppSettings
open Softellect.Sys.Core
open Softellect.Sys.Crypto
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Wcf.Common
open Softellect.Wcf.Errors
open Softellect.Wcf.Service
open Softellect.Wcf.Program
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.Errors
open Softellect.Vnc.Core.CryptoHelpers
open Softellect.Vnc.Core.ServiceInfo
open Softellect.Vnc.Core.AppSettings

module WcfServer =

    [<Literal>]
    let ProgramName = "VncService"


    let private toVncWcfErr (e: WcfError) : VncError = VncWcfErr (VncControlWcfErr e)


    /// Decrypt and verify an RSA-authenticated connect request.
    let inline private tryDecryptAndVerifyConnect (serverData: VncServerData) (data: byte[]) : Result<VncConnectRequest * PublicKey, VncError> =
        match tryDecrypt serverData.encryptionType data serverData.serverPrivateKey with
        | Ok r ->
            if r.Length < VncClientIdPrefixSize then
                VncGeneralErr $"Decrypted data too short: {r.Length} < {VncClientIdPrefixSize}." |> Error
            else
                let viewerIdBytes = r[0..VncClientIdPrefixSize - 1]
                let viewerId = Guid(viewerIdBytes) |> VncViewerId

                match tryLoadViewerPublicKey serverData.viewerKeysPath viewerId with
                | Some viewerKey ->
                    match tryVerify r viewerKey with
                    | Ok verified ->
                        let requestBytes = Array.sub verified VncClientIdPrefixSize (verified.Length - VncClientIdPrefixSize)
                        match tryDeserialize<VncConnectRequest> wcfSerializationFormat requestBytes with
                        | Ok request ->
                            if request.viewerId = viewerId then
                                Ok (request, viewerKey)
                            else
                                VncConnectionErr (AuthFailedErr $"ViewerId mismatch: expected {viewerId.value}, got {request.viewerId.value}") |> Error
                        | Error e -> VncGeneralErr $"Deserialize connect request failed: %A{e}" |> Error
                    | Error e -> VncGeneralErr $"tryVerify failed: %A{e}" |> Error
                | None ->
                    VncConnectionErr (AuthFailedErr $"Unknown viewer: {viewerId.value}") |> Error
        | Error e -> VncGeneralErr $"tryDecrypt failed: %A{e}" |> Error


    /// Sign and encrypt a connect response with RSA.
    let inline private trySignAndEncryptConnect (serverData: VncServerData) (viewerKey: PublicKey) (response: VncConnectResponse) : Result<byte[], VncError> =
        match trySerialize wcfSerializationFormat response with
        | Ok responseBytes ->
            match trySignAndEncrypt serverData.encryptionType responseBytes serverData.serverPrivateKey viewerKey with
            | Ok encrypted -> Ok encrypted
            | Error e -> VncGeneralErr $"trySignAndEncrypt failed: %A{e}" |> Error
        | Error e -> VncGeneralErr $"Serialize connect response failed: %A{e}" |> Error


    [<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, IncludeExceptionDetailInFaults = true)>]
    type VncWcfService(service: VncServiceImpl.VncService, serverData: VncServerData) =
        let iService = service :> IVncService

        let connectImpl (data: byte[]) : Result<byte[], VncError> =
            match tryDecryptAndVerifyConnect serverData data with
            | Ok (request, viewerKey) ->
                match iService.connect request with
                | Ok response ->
                    trySignAndEncryptConnect serverData viewerKey response
                | Error e -> Error e
            | Error e -> Error e

        let sessionHandler (handler: 'TReq -> VncResult<'TResp>) (data: byte[]) : Result<byte[], VncError> =
            match service.sessionAesKey with
            | Some sessionKey ->
                match tryDecryptSession sessionKey data with
                | Ok decrypted ->
                    match tryDeserialize wcfSerializationFormat decrypted with
                    | Ok request ->
                        let result = handler request
                        match trySerialize wcfSerializationFormat result with
                        | Ok responseBytes -> tryEncryptSession sessionKey responseBytes
                        | Error e -> VncGeneralErr $"Serialize session response failed: %A{e}" |> Error
                    | Error e -> VncGeneralErr $"Deserialize session request failed: %A{e}" |> Error
                | Error e -> Error e
            | None ->
                VncConnectionErr (AuthFailedErr "No active session") |> Error

        let sessionNoArgHandler (handler: unit -> VncResult<'TResp>) (_data: byte[]) : Result<byte[], VncError> =
            match service.sessionAesKey with
            | Some sessionKey ->
                let result = handler ()
                match trySerialize wcfSerializationFormat result with
                | Ok responseBytes -> tryEncryptSession sessionKey responseBytes
                | Error e -> VncGeneralErr $"Serialize session response failed: %A{e}" |> Error
            | None ->
                VncConnectionErr (AuthFailedErr "No active session") |> Error

        interface IVncWcfService with
            member _.connect data = tryReply connectImpl toVncWcfErr data
            member _.disconnect data = tryReply (sessionHandler iService.disconnect) toVncWcfErr data
            member _.sendInput data = tryReply (sessionHandler iService.sendInput) toVncWcfErr data
            member _.getClipboard data = tryReply (sessionNoArgHandler iService.getClipboard) toVncWcfErr data
            member _.setClipboard data = tryReply (sessionHandler iService.setClipboard) toVncWcfErr data
            member _.listDirectory data = tryReply (fun _ -> Error (VncGeneralErr "Not implemented")) toVncWcfErr data
            member _.readFileChunk data = tryReply (fun _ -> Error (VncGeneralErr "Not implemented")) toVncWcfErr data
            member _.writeFileChunk data = tryReply (fun _ -> Error (VncGeneralErr "Not implemented")) toVncWcfErr data


    let getVncWcfProgram (data: VncServerData) (getService: unit -> VncServiceImpl.VncService) argv =
        let serviceInstance = getService()

        let saveSettings() =
            Logger.logInfo "VNC Service settings save requested (no-op for now)."

        let projectName = getProjectName() |> Some
        let getWcfService (_: IVncService) = VncWcfService(serviceInstance, data)

        let programData : ProgramData<IVncService, VncWcfService> =
            {
                getService = fun () -> serviceInstance :> IVncService
                serviceAccessInfo = data.vncServiceAccessInfo.serviceAccessInfo
                getWcfService = getWcfService
                saveSettings = saveSettings
                configureServices = None
                configureServiceLogging = configureServiceLogging projectName
                configureLogging = configureLogging projectName
                postBuildHandler = None
            }

        fun () -> wcfMain<IVncService, IVncWcfService, VncWcfService> ProgramName programData argv
