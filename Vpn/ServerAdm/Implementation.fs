namespace Softellect.Vpn.ServerAdm

open System
open System.IO
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
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


    let private ensureDirectory (path: string) =
        if not (Directory.Exists path) then
            Directory.CreateDirectory path |> ignore


    let generateKeys (ctx: ServerAdmContext) (args: GenerateKeysArgs list) =
        let force = args |> List.tryPick (function Force f -> Some f) |> Option.defaultValue false
        let keyPath = ctx.serverAccessInfo.serverKeyPath.value
        let privateKeyFile = Path.Combine(keyPath, "server.key")
        let publicKeyFile = Path.Combine(keyPath, "server.pkx")

        if File.Exists(privateKeyFile) && not force then
            Logger.logWarn $"Keys already exist at {keyPath}. Use -f true to force regeneration."
            Error $"Keys already exist. Use -f true to force regeneration."
        else
            ensureDirectory keyPath
            let keyId = KeyId (Guid.NewGuid())
            let (publicKey, privateKey) = generateKey keyId

            File.WriteAllText(privateKeyFile, privateKey.value)
            File.WriteAllText(publicKeyFile, publicKey.value)

            Logger.logInfo $"Generated server keys at {keyPath}"
            Logger.logInfo $"Key ID: {keyId.value}"
            Ok $"Keys generated successfully. Key ID: {keyId.value}"


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
            let keyPath = ctx.serverAccessInfo.serverKeyPath.value
            let publicKeyFile = Path.Combine(keyPath, "server.pkx")

            if not (File.Exists publicKeyFile) then
                Error $"Server public key not found at {publicKeyFile}. Generate keys first."
            else
                let publicKeyXml = File.ReadAllText(publicKeyFile)
                let publicKey = PublicKey publicKeyXml

                match tryExportPublicKey (FolderName outputFolder) publicKey overwrite with
                | Ok () ->
                    Logger.logInfo $"Exported public key to {outputFolder}"
                    Ok $"Public key exported to {outputFolder}"
                | Error e ->
                    Logger.logError $"Failed to export public key: %A{e}"
                    Error $"Failed to export public key: %A{e}"


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
                let clientKeysPath = ctx.serverAccessInfo.clientKeysPath.value
                ensureDirectory clientKeysPath

                let targetFile = Path.Combine(clientKeysPath, $"{keyId.value}.pkx")
                File.WriteAllText(targetFile, publicKey.value)

                Logger.logInfo $"Imported client key: {keyId.value}"
                Ok $"Client key imported. Client ID: {keyId.value}"
            | Error e ->
                Logger.logError $"Failed to import client key: %A{e}"
                Error $"Failed to import client key: %A{e}"


    let registerClient (ctx: ServerAdmContext) (args: RegisterClientArgs list) =
        let clientIdStr = args |> List.tryPick (function ClientId id -> Some id | _ -> None) |> Option.defaultValue ""
        let clientName = args |> List.tryPick (function ClientName n -> Some n | _ -> None) |> Option.defaultValue ""
        let assignedIpStr = args |> List.tryPick (function AssignedIp ip -> Some ip | _ -> None) |> Option.defaultValue ""

        match VpnClientId.tryCreate clientIdStr with
        | Some clientId ->
            match VpnIpAddress.tryParse assignedIpStr with
            | Some assignedIp ->
                // For now, just log the registration. In a full implementation,
                // this would update a clients.json or database
                Logger.logInfo $"Registered client: {clientId.value}"
                Logger.logInfo $"  Name: {clientName}"
                Logger.logInfo $"  Assigned IP: {assignedIp.value}"
                Ok $"Client registered: {clientName} ({clientId.value}) -> {assignedIp.value}"
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
