namespace Softellect.Vpn.ClientAdm

open System
open System.IO
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.Core
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Core.KeyManagement
open Softellect.Wcf.Common
open Softellect.Vpn.ClientAdm.CommandLine

module Implementation =

    type ClientAdmContext =
        {
            clientAccessInfo : VpnClientAccessInfo
        }

        static member create() =
            {
                clientAccessInfo = loadVpnClientAccessInfo()
            }


    let generateKeys (ctx: ClientAdmContext) (args: GenerateKeysArgs list) =
        let force = args |> List.tryPick (function Force f -> Some f) |> Option.defaultValue false
        let keyFolder = ctx.clientAccessInfo.clientKeyPath

        match keyFolder.tryEnsureFolderExists() with
        | Ok () ->
            // Use the client ID as the key ID
            let keyId = KeyId ctx.clientAccessInfo.vpnClientId.value
            let (publicKey, privateKey) = generateKey keyId

            match tryExportPrivateKey keyFolder privateKey force with
            | Ok privateKeyFile ->
                match tryExportPublicKey keyFolder publicKey force with
                | Ok () ->
                    Logger.logInfo $"Generated client keys at {keyFolder.value}"
                    Logger.logInfo $"Client ID: {ctx.clientAccessInfo.vpnClientId.value}"
                    Logger.logInfo $"Private key: {privateKeyFile.value}"
                    Ok $"Keys generated successfully. Client ID: {ctx.clientAccessInfo.vpnClientId.value}"
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


    let exportPublicKey (ctx: ClientAdmContext) (args: ExportPublicKeyArgs list) =
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
            let keyFolder = ctx.clientAccessInfo.clientKeyPath

            // Find the .pkx file in the key folder
            let pkxFiles = Directory.GetFiles(keyFolder.value, "*.pkx")

            if pkxFiles.Length = 0 then
                Error $"No public key found in {keyFolder.value}. Generate keys first."
            else
                let sourceFile = FileName pkxFiles.[0]

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


    let importServerKey (ctx: ClientAdmContext) (args: ImportServerKeyArgs list) =
        let inputFile =
            args
            |> List.tryPick (function InputFileName f -> Some f)
            |> Option.defaultValue ""

        if String.IsNullOrWhiteSpace inputFile then
            Error "Input file name is required."
        else
            match tryImportPublicKey (FileName inputFile) None with
            | Ok (keyId, publicKey) ->
                // serverPublicKeyPath is a folder where we store server keys
                let targetFolder = ctx.clientAccessInfo.serverPublicKeyPath

                match targetFolder.tryEnsureFolderExists() with
                | Ok () ->
                    match tryExportPublicKey targetFolder publicKey true with
                    | Ok () ->
                        Logger.logInfo $"Imported server key: {keyId.value}"
                        Logger.logInfo $"Saved to: {targetFolder.value}"
                        Ok $"Server key imported. Server Key ID: {keyId.value}"
                    | Error e ->
                        Logger.logError $"Failed to save server key: %A{e}"
                        Error $"Failed to save server key: %A{e}"
                | Error e ->
                    Logger.logError $"Failed to create server key folder: %A{e}"
                    Error $"Failed to create server key folder: %A{e}"
            | Error e ->
                Logger.logError $"Failed to import server key: %A{e}"
                Error $"Failed to import server key: %A{e}"


    let status (ctx: ClientAdmContext) (args: StatusArgs list) =
        let verbose = args |> List.tryPick (function Verbose v -> Some v) |> Option.defaultValue false

        Logger.logInfo "VPN Client Configuration:"
        Logger.logInfo $"  Client ID: {ctx.clientAccessInfo.vpnClientId.value}"

        match ctx.clientAccessInfo.serverAccessInfo with
        | NetTcpServiceInfo info ->
            Logger.logInfo $"  Server: {info.netTcpServiceAddress.value}:{info.netTcpServicePort.value}"
            Logger.logInfo $"  Protocol: NetTcp"
            Logger.logInfo $"  Security: {info.netTcpSecurityMode}"
        | HttpServiceInfo info ->
            Logger.logInfo $"  Server: {info.httpServiceAddress.value}:{info.httpServicePort.value}"
            Logger.logInfo $"  Protocol: HTTP"

        let clientKeyFolder = ctx.clientAccessInfo.clientKeyPath.value
        let keyFiles = if Directory.Exists clientKeyFolder then Directory.GetFiles(clientKeyFolder, "*.key") else [||]
        let pkxFiles = if Directory.Exists clientKeyFolder then Directory.GetFiles(clientKeyFolder, "*.pkx") else [||]
        let clientKeysStatus = if keyFiles.Length > 0 && pkxFiles.Length > 0 then "OK" else "MISSING"
        Logger.logInfo $"  Client Keys: {clientKeysStatus}"

        let serverKeyFolder = ctx.clientAccessInfo.serverPublicKeyPath.value
        let serverKeyFiles = if Directory.Exists serverKeyFolder then Directory.GetFiles(serverKeyFolder, "*.pkx") else [||]
        let serverKeyStatus = if serverKeyFiles.Length > 0 then "OK" else "MISSING"
        Logger.logInfo $"  Server Key: {serverKeyStatus}"

        if verbose then
            Logger.logInfo $"  Client Key Path: {clientKeyFolder}"
            Logger.logInfo $"  Server Key Path: {serverKeyFolder}"
            Logger.logInfo "  Local LAN Exclusions:"
            for exclusion in ctx.clientAccessInfo.localLanExclusions do
                Logger.logInfo $"    - {exclusion.value}"

        Ok "Status check complete."


    let setServer (ctx: ClientAdmContext) (args: SetServerArgs list) =
        let address = args |> List.tryPick (function Address a -> Some a | _ -> None) |> Option.defaultValue ""
        let port = args |> List.tryPick (function Port p -> Some p | _ -> None) |> Option.defaultValue 5080

        if String.IsNullOrWhiteSpace address then
            Error "Server address is required."
        else
            let newServerInfo =
                {
                    netTcpServiceAddress = ServiceAddress (Ip4 address)
                    netTcpServicePort = ServicePort port
                    netTcpServiceName = ServiceName "VpnService"
                    netTcpSecurityMode = NoSecurity
                }
                |> NetTcpServiceInfo

            let updatedClientInfo =
                { ctx.clientAccessInfo with serverAccessInfo = newServerInfo }

            match updateVpnClientAccessInfo updatedClientInfo with
            | Ok () ->
                Logger.logInfo $"Server configured: {address}:{port}"
                Ok $"Server configured successfully: {address}:{port}"
            | Error e ->
                Logger.logError $"Failed to update configuration: %A{e}"
                Error $"Failed to update configuration: %A{e}"
