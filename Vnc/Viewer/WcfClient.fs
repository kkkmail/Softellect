namespace Softellect.Vnc.Viewer

open Softellect.Sys.Core
open Softellect.Sys.Crypto
open Softellect.Sys.Logging
open Softellect.Wcf.Client
open Softellect.Wcf.Common
open Softellect.Wcf.Errors
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.Errors
open Softellect.Vnc.Core.CryptoHelpers
open Softellect.Vnc.Core.ServiceInfo

module WcfClient =

    let private toVncWcfErr (e: WcfError) : VncError = VncWcfErr (VncControlWcfErr e)


    /// Sign and encrypt a connect request with RSA (like VPN auth).
    let inline private trySignAndEncryptConnect (viewerData: VncViewerData) (request: VncConnectRequest) =
        match trySerialize wcfSerializationFormat request with
        | Ok requestBytes ->
            let viewerIdBytes = viewerData.viewerId.value.ToByteArray()
            let toBeEncrypted = Array.append viewerIdBytes requestBytes
            match trySignAndEncrypt viewerData.encryptionType toBeEncrypted viewerData.viewerPrivateKey viewerData.serverPublicKey with
            | Ok encrypted -> Ok encrypted
            | Error e -> VncGeneralErr $"trySignAndEncrypt failed: %A{e}" |> Error
        | Error e -> VncGeneralErr $"Serialize connect request failed: %A{e}" |> Error


    /// Decrypt and verify a connect response with RSA.
    let inline private tryDecryptAndVerifyConnect<'T> (viewerData: VncViewerData) (response: byte[]) =
        match tryDecryptAndVerify viewerData.encryptionType response viewerData.viewerPrivateKey viewerData.serverPublicKey with
        | Ok verified ->
            match tryDeserialize<'T> wcfSerializationFormat verified with
            | Ok result -> Ok result
            | Error e -> VncGeneralErr $"Deserialize connect response failed: %A{e}" |> Error
        | Error e -> VncGeneralErr $"tryDecryptAndVerify failed: %A{e}" |> Error


    type VncWcfClient(viewerData: VncViewerData, serviceAccessInfo: ServiceAccessInfo) =
        let url = serviceAccessInfo.getUrl()
        let commType = serviceAccessInfo.communicationType
        let tryGetService () = tryGetWcfService<IVncWcfService> commType url
        let mutable sessionAesKey : byte[] option = None

        /// Encrypted connect using RSA sign+encrypt.
        member _.connect (request: VncConnectRequest) : VncResult<VncConnectResponse> =
            match trySignAndEncryptConnect viewerData request with
            | Ok encrypted ->
                let wcfResult =
                    tryCommunicate tryGetService (fun s -> s.connect) toVncWcfErr encrypted
                    |> Result.bind (tryDecryptAndVerifyConnect<VncConnectResponse> viewerData)
                match wcfResult with
                | Ok response ->
                    sessionAesKey <- Some response.sessionAesKey
                    Ok response
                | Error e -> Error e
            | Error e -> Error e

        /// Post-auth encrypted methods using session AES key.
        member private _.sessionCall<'TReq, 'TResp> (getMethod: IVncWcfService -> byte[] -> byte[]) (request: 'TReq) : VncResult<'TResp> =
            match sessionAesKey with
            | Some key ->
                match trySerialize wcfSerializationFormat request with
                | Ok reqBytes ->
                    match tryEncryptSession key reqBytes with
                    | Ok encrypted ->
                        match tryCommunicate tryGetService (fun s -> getMethod s) toVncWcfErr encrypted with
                        | Ok responseBytes ->
                            match tryDecryptSession key responseBytes with
                            | Ok decrypted ->
                                match tryDeserialize<VncResult<'TResp>> wcfSerializationFormat decrypted with
                                | Ok (Ok v) -> Ok v
                                | Ok (Error e) -> Error e
                                | Error e -> VncGeneralErr $"Deserialize session response failed: %A{e}" |> Error
                            | Error e -> Error e
                        | Error e -> Error e
                    | Error e -> Error e
                | Error e -> VncGeneralErr $"Serialize session request failed: %A{e}" |> Error
            | None ->
                VncConnectionErr (AuthFailedErr "No active session") |> Error

        member this.disconnect (sessionId: VncSessionId) : VncUnitResult =
            this.sessionCall<VncSessionId, unit> (fun s -> s.disconnect) sessionId

        member this.sendInput (event: InputEvent) : VncUnitResult =
            this.sessionCall<InputEvent, unit> (fun s -> s.sendInput) event

        member _.getClipboard () : VncResult<ClipboardData> =
            match sessionAesKey with
            | Some key ->
                match tryCommunicate tryGetService (fun s -> s.getClipboard) toVncWcfErr [||] with
                | Ok responseBytes ->
                    match tryDecryptSession key responseBytes with
                    | Ok decrypted ->
                        match tryDeserialize<VncResult<ClipboardData>> wcfSerializationFormat decrypted with
                        | Ok (Ok v) -> Ok v
                        | Ok (Error e) -> Error e
                        | Error e -> VncGeneralErr $"Deserialize clipboard response failed: %A{e}" |> Error
                    | Error e -> Error e
                | Error e -> Error e
            | None ->
                VncConnectionErr (AuthFailedErr "No active session") |> Error

        member this.setClipboard (data: ClipboardData) : VncUnitResult =
            this.sessionCall<ClipboardData, unit> (fun s -> s.setClipboard) data

        member _.currentSessionAesKey = sessionAesKey
