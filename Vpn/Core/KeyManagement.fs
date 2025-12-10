namespace Softellect.Vpn.Core

open System.IO
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.Sys.Core
open Softellect.Sys.Crypto

module KeyManagement =

    let private privateKeyExtension = FileExtension ".key"
    let private toError e = e |> CryptoErr |> Error


    /// Exports a private key to a file in the specified folder.
    /// The file will be named with the KeyId GUID and .key extension.
    let tryExportPrivateKey (folderName : FolderName) (PrivateKey privateKey as pk) (overwrite : bool) =
        try
            match extractKeyIdFromKey privateKey with
            | Some i ->
                let fileName = (FileName $"{i.value}{privateKeyExtension.value}").combine(folderName)
                let keyValue = privateKey |> zip

                if overwrite || (File.Exists fileName.value |> not)
                then
                    File.WriteAllBytes(fileName.value, keyValue)
                    Ok fileName
                else fileName |> KeyFileExistErr |> toError
            | None -> MissingKeyId |> toError
        with
        | e -> e |> KeyExportExn |> toError


    /// Imports a private key from a file.
    let tryImportPrivateKey (fileName : FileName) (ko : KeyId option) =
        try
            match fileName.tryGetFullFileName() with
            | Ok fn ->
                let key = File.ReadAllBytes fn.value |> unZip |> PrivateKey

                match ko with
                | Some k ->
                    match extractKeyIdFromKey key.value with
                    | Some embeddedId when embeddedId = k -> Ok (k, key)
                    | Some _ -> (k, fileName) |> KeyMismatchErr |> toError
                    | None -> KeyImportMissingIdErr |> toError
                | None ->
                    match extractKeyIdFromKey key.value with
                    | Some k -> Ok (k, key)
                    | None -> KeyImportMissingIdErr |> toError
            | Error e -> e |> KeyImportFileErr |> toError
        with
        | e -> e |> KeyImportExn |> toError
