namespace Softellect.Vnc.Core

open System
open System.IO
open System.Security.Cryptography
open Softellect.Sys.Core
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.Sys.Crypto
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.Errors

module CryptoHelpers =

    /// Generate a 32-byte session AES key.
    let generateSessionAesKey () : byte[] =
        use aes = Aes.Create()
        aes.KeySize <- 256
        aes.GenerateKey()
        aes.Key

    /// Encrypt data with a session AES key, prepending a random 16-byte IV.
    /// Output format: [IV (16 bytes)] [AES-CBC encrypted data]
    let tryEncryptSession (sessionKey: byte[]) (data: byte[]) : VncResult<byte[]> =
        try
            use aes = Aes.Create()
            aes.Key <- sessionKey
            aes.GenerateIV()
            let iv = aes.IV
            use encryptor = aes.CreateEncryptor()
            use ms = new MemoryStream()
            use cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write)
            cs.Write(data, 0, data.Length)
            cs.FlushFinalBlock()
            let encrypted = ms.ToArray()
            Ok (Array.append iv encrypted)
        with
        | ex -> Error (VncCryptoErr (TryEncryptAesKeyExn ex))

    /// Decrypt data encrypted with tryEncryptSession.
    /// Input format: [IV (16 bytes)] [AES-CBC encrypted data]
    let tryDecryptSession (sessionKey: byte[]) (data: byte[]) : VncResult<byte[]> =
        try
            if data.Length < 16 then
                Error (VncCryptoErr (TryDecryptAesKeyExn (InvalidOperationException "Data too short for AES decryption")))
            else
                let iv = data[0..15]
                let encrypted = data[16..]
                use aes = Aes.Create()
                aes.Key <- sessionKey
                aes.IV <- iv
                use decryptor = aes.CreateDecryptor()
                use ms = new MemoryStream()
                use cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write)
                cs.Write(encrypted, 0, encrypted.Length)
                cs.FlushFinalBlock()
                Ok (ms.ToArray())
        with
        | ex -> Error (VncCryptoErr (TryDecryptAesKeyExn ex))

    /// Import a private key from a zipped XML file.
    let tryImportPrivateKey (fileName: FileName) (expectedKeyId: KeyId option) =
        try
            match fileName.tryGetFullFileName() with
            | Ok fn ->
                let key = File.ReadAllBytes fn.value |> unZip |> PrivateKey
                match expectedKeyId with
                | Some k ->
                    match extractKeyIdFromKey key.value with
                    | Some embeddedId when embeddedId = k -> Ok (k, key)
                    | Some _ -> (k, fileName) |> KeyMismatchErr |> CryptoErr |> Error
                    | None -> KeyImportMissingIdErr |> CryptoErr |> Error
                | None ->
                    match extractKeyIdFromKey key.value with
                    | Some k -> Ok (k, key)
                    | None -> KeyImportMissingIdErr |> CryptoErr |> Error
            | Error e -> e |> KeyImportFileErr |> CryptoErr |> Error
        with
        | e -> e |> KeyImportExn |> CryptoErr |> Error

    /// Export a private key to a zipped XML file.
    let tryExportPrivateKey (folderName: FolderName) (PrivateKey privateKey) (overwrite: bool) =
        try
            match extractKeyIdFromKey privateKey with
            | Some i ->
                let fileName = (FileName $"{i.value}.key").combine(folderName)
                let keyValue = privateKey |> zip
                if overwrite || (File.Exists fileName.value |> not) then
                    File.WriteAllBytes(fileName.value, keyValue)
                    Ok fileName
                else fileName |> KeyFileExistErr |> CryptoErr |> Error
            | None -> MissingKeyId |> CryptoErr |> Error
        with
        | e -> e |> KeyExportExn |> CryptoErr |> Error

    /// Load a viewer's public key by viewerId from the keys folder.
    let tryLoadViewerPublicKey (keysPath: FolderName) (viewerId: VncViewerId) =
        let keyId = KeyId viewerId.value
        let keyFileName = FileName $"{viewerId.value}.pkx"
        let keyFilePath = keyFileName.combine keysPath

        match tryImportPublicKey keyFilePath (Some keyId) with
        | Ok (_, publicKey) -> Some publicKey
        | Error e ->
            Logger.logWarn $"Failed to load public key for viewer {viewerId.value}: '%A{e}'"
            None

    /// Load server keys (private + public) from a folder.
    let loadServerKeys (serverKeyPath: FolderName) =
        if not (Directory.Exists serverKeyPath.value) then
            Logger.logError $"Server key folder not found: {serverKeyPath.value}"
            Error $"Server key folder not found: {serverKeyPath.value}. Generate keys first."
        else
            let keyFiles = Directory.GetFiles(serverKeyPath.value, "*.key")
            let pkxFiles = Directory.GetFiles(serverKeyPath.value, "*.pkx")

            if keyFiles.Length = 0 || pkxFiles.Length = 0 then
                Logger.logError $"Server keys not found in {serverKeyPath.value}"
                Error $"Server keys not found in {serverKeyPath.value}. Generate keys first."
            else
                match tryImportPublicKey (FileName pkxFiles[0]) None with
                | Ok (keyId, publicKey) ->
                    match tryImportPrivateKey (FileName keyFiles[0]) (Some keyId) with
                    | Ok (_, privateKey) -> Ok (privateKey, publicKey)
                    | Error e ->
                        Logger.logError $"Failed to import server private key: %A{e}"
                        Error $"Failed to import server private key: %A{e}"
                | Error e ->
                    Logger.logError $"Failed to import server public key: %A{e}"
                    Error $"Failed to import server public key: %A{e}"

    /// Load viewer keys (private + public + server public) from paths.
    let loadViewerKeys (viewerKeyPath: FolderName) (serverPkxPath: FileName) =
        let keyFiles = Directory.GetFiles(viewerKeyPath.value, "*.key")
        let pkxFiles = Directory.GetFiles(viewerKeyPath.value, "*.pkx")

        if keyFiles.Length = 0 || pkxFiles.Length = 0 then
            Error $"Viewer keys not found in {viewerKeyPath.value}. Generate keys first."
        else
            match tryImportPublicKey (FileName pkxFiles[0]) None with
            | Ok (keyId, viewerPublicKey) ->
                match tryImportPrivateKey (FileName keyFiles[0]) (Some keyId) with
                | Ok (_, viewerPrivateKey) ->
                    match tryImportPublicKey serverPkxPath None with
                    | Ok (_, serverPublicKey) ->
                        let viewerId = VncViewerId keyId.value
                        Ok (viewerId, viewerPrivateKey, viewerPublicKey, serverPublicKey)
                    | Error e -> Error $"Failed to import server public key: %A{e}"
                | Error e -> Error $"Failed to import viewer private key: %A{e}"
            | Error e -> Error $"Failed to import viewer public key: %A{e}"
