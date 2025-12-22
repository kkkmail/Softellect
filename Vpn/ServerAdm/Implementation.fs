namespace Softellect.Vpn.ServerAdm

open System
open System.IO
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.Sys.Crypto
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.KeyManagement
open Softellect.Vpn.ServerAdm.CommandLine

module Implementation =

    type ServerAdmContext =
        {
            serverAccessInfo : Softellect.Vpn.Core.ServiceInfo.VpnServerAccessInfo
        }

        static member create() =
            {
                serverAccessInfo = loadVpnServerAccessInfo()
            }


    let generateKeys (ctx: ServerAdmContext) (args: GenerateKeysArgs list) =
        let force = args |> List.tryPick (function Force f -> Some f) |> Option.defaultValue false
        let keyFolder = ctx.serverAccessInfo.serverKeyPath
        let keyId = KeyId ctx.serverAccessInfo.vpnServerId.value

        match keyFolder.tryEnsureFolderExists() with
        | Ok () ->
            let (publicKey, privateKey) = generateKey keyId

            match tryExportPrivateKey keyFolder privateKey force with
            | Ok privateKeyFile ->
                match tryExportPublicKey keyFolder publicKey force with
                | Ok () ->
                    Logger.logInfo $"Generated server keys at {keyFolder.value}"
                    Logger.logInfo $"Server ID: {ctx.serverAccessInfo.vpnServerId.value}"
                    Logger.logInfo $"Private key: {privateKeyFile.value}"
                    Ok $"Keys generated successfully. Server ID: {ctx.serverAccessInfo.vpnServerId.value}"
                | Error e ->
                    // Clean up private key file on failure
                    try File.Delete(privateKeyFile.value) with | _ -> ()
                    Logger.logError $"Failed to export public key: %A{e}"
                    Error $"Failed to export public key: %A{e}"
            | Error e ->
                Logger.logError $"Failed to export private key: %A{e}"
                Error $"Failed to export private key: %A{e}"
        | Error e ->
            Logger.logError $"Failed to create key folder: %A{e}"
            Error $"Failed to create key folder: %A{e}"


    let exportPublicKey (ctx: ServerAdmContext) (args: ExportPublicKeyArgs list) =
        let outputFolder =
            args
            |> List.tryPick (function OutputFolderName f -> Some f | _ -> None)
            |> Option.defaultValue ""

        let overwrite =
            args
            |> List.tryPick (function Overwrite o -> Some o | _ -> None)
            |> Option.defaultValue false

        if String.IsNullOrWhiteSpace outputFolder then
            Error "Output folder name is required."
        else
            let keyFolder = ctx.serverAccessInfo.serverKeyPath

            // Find the .pkx file in the key folder
            let pkxFiles = Directory.GetFiles(keyFolder.value, "*.pkx")

            if pkxFiles.Length = 0 then
                Error $"No public key found in {keyFolder.value}. Generate keys first."
            else
                let sourceFile = FileName pkxFiles[0]

                match tryImportPublicKey sourceFile None with
                | Ok (keyId, publicKey) ->
                    let outputFolderName = FolderName outputFolder

                    match outputFolderName.tryEnsureFolderExists() with
                    | Ok () ->
                        match tryExportPublicKey outputFolderName publicKey overwrite with
                        | Ok () ->
                            Logger.logInfo $"Exported public key {keyId.value} to {outputFolder}"
                            Ok $"Public key exported to {outputFolder}"
                        | Error e ->
                            Logger.logError $"Failed to export public key: %A{e}"
                            Error $"Failed to export public key: %A{e}"
                    | Error e ->
                        Logger.logError $"Failed to create output folder: %A{e}"
                        Error $"Failed to create output folder: %A{e}"
                | Error e ->
                    Logger.logError $"Failed to read public key: %A{e}"
                    Error $"Failed to read public key: %A{e}"


    let importClientKey (ctx: ServerAdmContext) (args: ImportClientKeyArgs list) =
        let inputFile =
            args
            |> List.tryPick (function InputFileName f -> Some f)
            |> Option.defaultValue ""

        if String.IsNullOrWhiteSpace inputFile then
            Error "Input file name is required."
        else
            match tryImportPublicKey (FileName inputFile) None with
            | Ok (keyId, publicKey) ->
                let clientKeysFolder = ctx.serverAccessInfo.clientKeysPath

                match clientKeysFolder.tryEnsureFolderExists() with
                | Ok () ->
                    match tryExportPublicKey clientKeysFolder publicKey true with
                    | Ok () ->
                        Logger.logInfo $"Imported client key: {keyId.value}"
                        Ok $"Client key imported. Client ID: {keyId.value}"
                    | Error e ->
                        Logger.logError $"Failed to save client key: %A{e}"
                        Error $"Failed to save client key: %A{e}"
                | Error e ->
                    Logger.logError $"Failed to create client keys folder: %A{e}"
                    Error $"Failed to create client keys folder: %A{e}"
            | Error e ->
                Logger.logError $"Failed to import client key: %A{e}"
                Error $"Failed to import client key: %A{e}"


    let registerClient (ctx: ServerAdmContext) (args: RegisterClientArgs list) =
        let clientIdStr = args |> List.tryPick (function ClientId id -> Some id | _ -> None) |> Option.defaultValue ""
        let clientName = args |> List.tryPick (function ClientName n -> Some n | _ -> None) |> Option.defaultValue ""
        let assignedIpStr = args |> List.tryPick (function AssignedIp ip -> Some ip | _ -> None) |> Option.defaultValue ""

        match VpnClientId.tryCreate clientIdStr with
        | Some clientId ->
            match VpnIpAddress.tryCreate assignedIpStr with
            | Some assignedIp ->
                let config =
                    {
                        clientId = clientId
                        clientData =
                            {
                                clientName = VpnClientName clientName
                                assignedIp = assignedIp
                                vpnTransportProtocol = getVpnTransportProtocol()
                                useEncryption = true
                                encryptionType = EncryptionType.defaultValue
                            }
                    }

                match addOrUpdateVpnClientConfig config with
                | Ok () ->
                    Logger.logInfo $"Registered client: {clientId.value}"
                    Logger.logInfo $"  Name: {clientName}"
                    Logger.logInfo $"  Assigned IP: {assignedIp.value}"
                    Ok $"Client registered: {clientName} ({clientId.value}) -> {assignedIp.value}"
                | Error e ->
                    Logger.logError $"Failed to save client config: %A{e}"
                    Error $"Failed to save client config: %A{e}"
            | None ->
                Error $"Invalid IP address: {assignedIpStr}"
        | None ->
            Error $"Invalid client ID (must be a GUID): {clientIdStr}"


    let listClients (ctx: ServerAdmContext) (args: ListClientsArgs list) =
        let verbose = args |> List.tryPick (function Verbose v -> Some v) |> Option.defaultValue false
        let clientKeysPath = ctx.serverAccessInfo.clientKeysPath.value

        if Directory.Exists clientKeysPath then
            let files = Directory.GetFiles(clientKeysPath, "*.pkx")

            if files.Length = 0 then
                Logger.logInfo "No client keys found."
                Ok "No clients registered."
            else
                Logger.logInfo $"Found {files.Length} client(s):"

                for file in files do
                    let fileName = Path.GetFileNameWithoutExtension(file)
                    Logger.logInfo $"  - {fileName}"

                    if verbose then
                        match tryImportPublicKey (FileName file) None with
                        | Ok (keyId, _) ->
                            Logger.logInfo $"      Key ID: {keyId.value}"
                        | Error _ ->
                            Logger.logInfo $"      (Could not read key details)"

                Ok $"Listed {files.Length} client(s)."
        else
            Logger.logInfo $"Client keys directory does not exist: {clientKeysPath}"
            Ok "No clients registered (directory does not exist)."
