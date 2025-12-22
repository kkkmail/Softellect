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
            let publicKey, privateKey = generateKey keyId

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


    let importServerKey (ctx: ClientAdmContext) (args: ImportServerKeyArgs list) =
        let inputFile =
            args
            |> List.tryPick (function InputFileName f -> Some f)
            |> Option.defaultValue ""

        if String.IsNullOrWhiteSpace inputFile then
            Error "Input file name is required."
        else
            let sourceFileName = FileName inputFile

            match sourceFileName.tryGetFullFileName() with
            | Ok fullSourcePath ->
                if not (File.Exists fullSourcePath.value) then
                    Error $"Input file not found: {inputFile}"
                else
                    // Import the key to verify and extract server ID
                    match tryImportPublicKey fullSourcePath None with
                    | Ok (keyId, publicKey) ->
                        let targetFolder = ctx.clientAccessInfo.serverPublicKeyPath

                        match targetFolder.tryEnsureFolderExists() with
                        | Ok () ->
                            match tryExportPublicKey targetFolder publicKey true with
                            | Ok () ->
                                // Update the VpnServerId in settings to match the imported key
                                let vpnServerId = VpnServerId keyId.value
                                let updatedClientInfo = { ctx.clientAccessInfo with vpnServerId = vpnServerId }

                                match updateVpnClientAccessInfo updatedClientInfo with
                                | Ok () ->
                                    Logger.logInfo $"Imported server key: {keyId.value}"
                                    Logger.logInfo $"Updated VpnServerId in settings"
                                    Ok $"Server key imported. Server ID: {keyId.value}"
                                | Error e ->
                                    Logger.logError $"Failed to update settings: %A{e}"
                                    Error $"Key imported but failed to update settings: %A{e}"
                            | Error e ->
                                Logger.logError $"Failed to export server key: %A{e}"
                                Error $"Failed to export server key: %A{e}"
                        | Error e ->
                            Logger.logError $"Failed to create target folder: %A{e}"
                            Error $"Failed to create target folder: %A{e}"
                    | Error e ->
                        Logger.logError $"Failed to import server key: %A{e}"
                        Error $"Failed to import server key: %A{e}"
            | Error e ->
                Logger.logError $"Invalid input file path: %A{e}"
                Error $"Invalid input file path: %A{e}"


    let status (ctx: ClientAdmContext) (args: StatusArgs list) =
        let verbose = args |> List.tryPick (function Verbose v -> Some v) |> Option.defaultValue false

        Logger.logInfo "VPN Client Configuration:"
        Logger.logInfo $"  Client ID: {ctx.clientAccessInfo.vpnClientId.value}"
        Logger.logInfo $"  Server ID: {ctx.clientAccessInfo.vpnServerId.value}"

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
        let serverKeyFile = Path.Combine(serverKeyFolder, $"{ctx.clientAccessInfo.vpnServerId.value}.pkx")
        let serverKeyStatus = if File.Exists serverKeyFile then "OK" else "MISSING"
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


    let private tryParseDefaultRoute (routeOutput: string) =
        // Parse output of: netsh interface ipv4 show route
        // Looking for a line with destination 0.0.0.0/0
        // Format: Publish  Type      Met  Prefix                    Idx  Gateway/Interface Name
        //         No       Manual    1    0.0.0.0/0                   6  192.168.2.1
        let lines = routeOutput.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)

        lines
        |> Array.tryPick (fun line ->
            let parts = line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
            // Look for 0.0.0.0/0 in the line
            let hasDefaultRoute = parts |> Array.exists (fun p -> p = "0.0.0.0/0")
            if hasDefaultRoute && parts.Length >= 6 then
                // Last part is gateway IP, second to last is interface index
                let gateway = parts[parts.Length - 1]
                let interfaceIdx = parts[parts.Length - 2]
                match Int32.TryParse(interfaceIdx) with
                | true, idx -> Some (idx, gateway)
                | false, _ ->
                    // Maybe gateway is at different position, try to find IP-like strings
                    let ipParts = parts |> Array.filter (fun p ->
                        match IpAddress.tryCreate p with
                        | Some _ -> true
                        | None -> false)
                    if ipParts.Length > 0 then
                        // Try to get interface index from parts before gateway
                        let idxCandidates = parts |> Array.choose (fun p ->
                            match Int32.TryParse(p) with
                            | true, v when v > 0 && v < 1000 -> Some v
                            | _ -> None)
                        if idxCandidates.Length > 0 then
                            Some (idxCandidates[0], ipParts[ipParts.Length - 1])
                        else None
                    else None
            else None)


    let private tryGetInterfaceName (interfaceIdx: int) =
        // Parse output of: netsh interface ipv4 show interfaces
        // Format: Idx     Met         MTU          State                Name
        //           6      25        1500  connected     Wi-Fi
        match tryExecuteFile (FileName "netsh") "interface ipv4 show interfaces" with
        | Ok (_, output) ->
            let lines = output.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            lines
            |> Array.tryPick (fun line ->
                let parts = line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
                if parts.Length >= 5 then
                    match Int32.TryParse(parts[0]) with
                    | true, idx when idx = interfaceIdx ->
                        // Interface name is the last part (may contain spaces, so take everything after State)
                        // Find "connected" or similar state words, name is after
                        let stateIdx = parts |> Array.tryFindIndex (fun p ->
                            p.ToLower() = "connected" || p.ToLower() = "disconnected")
                        match stateIdx with
                        | Some si when si < parts.Length - 1 ->
                            let nameParts = parts |> Array.skip (si + 1)
                            Some (String.Join(" ", nameParts))
                        | _ -> Some parts[parts.Length - 1]
                    | _ -> None
                else None)
            |> Option.map Ok
            |> Option.defaultValue (Error $"Interface with index {interfaceIdx} not found")
        | Error e -> Error $"%A{e}"


    let detectPhysicalNetwork (_ctx: ClientAdmContext) =
        Logger.logInfo "Detecting physical network configuration..."

        match tryExecuteFile (FileName "netsh") "interface ipv4 show route" with
        | Ok (_, routeOutput) ->
            match tryParseDefaultRoute routeOutput with
            | Some (interfaceIdx, gatewayIp) ->
                Logger.logInfo $"Detected default gateway: {gatewayIp} (interface index: {interfaceIdx})"

                match tryGetInterfaceName interfaceIdx with
                | Ok interfaceName ->
                    Logger.logInfo $"Detected interface name: {interfaceName}"

                    match IpAddress.tryCreate gatewayIp with
                    | Some ip ->
                        Logger.logInfo $"Writing to appsettings.json: PhysicalGatewayIp={gatewayIp}, PhysicalInterfaceName={interfaceName}"

                        match tryWritePhysicalNetworkConfig ip interfaceName with
                        | Ok () ->
                            Logger.logInfo "Physical network configuration saved successfully."
                            Ok $"Detected and saved: PhysicalGatewayIp={gatewayIp}, PhysicalInterfaceName={interfaceName}"
                        | Error e ->
                            Logger.logError $"Failed to write config: %A{e}"
                            Error $"Failed to write config: %A{e}"
                    | None ->
                        Logger.logError $"Invalid gateway IP format: {gatewayIp}"
                        Error $"Invalid gateway IP format: {gatewayIp}"
                | Error e ->
                    Logger.logError $"Failed to get interface name: {e}"
                    Error $"Failed to get interface name: {e}"
            | None ->
                Logger.logError "Could not find default route (0.0.0.0/0) in routing table"
                Error "Could not find default route (0.0.0.0/0) in routing table"
        | Error e ->
            Logger.logError $"Failed to get routing table: %A{e}"
            Error $"Failed to get routing table: %A{e}"
