namespace Softellect.Vpn.ClientAdm

open System
open System.IO
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
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


    let private ensureDirectory (path: string) =
        if not (Directory.Exists path) then
            Directory.CreateDirectory path |> ignore


    let generateKeys (ctx: ClientAdmContext) (args: GenerateKeysArgs list) =
        let force = args |> List.tryPick (function Force f -> Some f) |> Option.defaultValue false
        let keyPath = ctx.clientAccessInfo.clientKeyPath.value
        let privateKeyFile = Path.Combine(keyPath, "client.key")
        let publicKeyFile = Path.Combine(keyPath, "client.pkx")

        if File.Exists(privateKeyFile) && not force then
            Logger.logWarn $"Keys already exist at {keyPath}. Use -f true to force regeneration."
            Error $"Keys already exist. Use -f true to force regeneration."
        else
            ensureDirectory keyPath

            // Use the client ID as the key ID
            let keyId = KeyId ctx.clientAccessInfo.vpnClientId.value
            let (publicKey, privateKey) = generateKey keyId

            File.WriteAllText(privateKeyFile, privateKey.value)
            File.WriteAllText(publicKeyFile, publicKey.value)

            Logger.logInfo $"Generated client keys at {keyPath}"
            Logger.logInfo $"Client ID: {ctx.clientAccessInfo.vpnClientId.value}"
            Ok $"Keys generated successfully. Client ID: {ctx.clientAccessInfo.vpnClientId.value}"


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
            let keyPath = ctx.clientAccessInfo.clientKeyPath.value
            let publicKeyFile = Path.Combine(keyPath, "client.pkx")

            if not (File.Exists publicKeyFile) then
                Error $"Client public key not found at {publicKeyFile}. Generate keys first."
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
                let targetPath = ctx.clientAccessInfo.serverPublicKeyPath.value
                let targetDir = Path.GetDirectoryName(targetPath)
                ensureDirectory targetDir

                File.WriteAllText(targetPath, publicKey.value)

                Logger.logInfo $"Imported server key: {keyId.value}"
                Logger.logInfo $"Saved to: {targetPath}"
                Ok $"Server key imported. Server Key ID: {keyId.value}"
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

        let clientKeyPath = ctx.clientAccessInfo.clientKeyPath.value
        let privateKeyExists = File.Exists(Path.Combine(clientKeyPath, "client.key"))
        let publicKeyExists = File.Exists(Path.Combine(clientKeyPath, "client.pkx"))
        let clientKeysStatus = if privateKeyExists && publicKeyExists then "OK" else "MISSING"
        Logger.logInfo $"  Client Keys: {clientKeysStatus}"

        let serverKeyExists = File.Exists(ctx.clientAccessInfo.serverPublicKeyPath.value)
        let serverKeyStatus = if serverKeyExists then "OK" else "MISSING"
        Logger.logInfo $"  Server Key: {serverKeyStatus}"

        if verbose then
            Logger.logInfo $"  Client Key Path: {clientKeyPath}"
            Logger.logInfo $"  Server Key Path: {ctx.clientAccessInfo.serverPublicKeyPath.value}"
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
