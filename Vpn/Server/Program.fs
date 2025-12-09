namespace Softellect.Vpn.Server

open System.IO
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.AppSettings
open Softellect.Wcf.Program
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.KeyManagement
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Server.Service

module Program =

    let private loadServerKeys (serverKeyPath: FolderName) =
        if not (Directory.Exists serverKeyPath.value) then
            Logger.logError $"Server key folder not found: {serverKeyPath.value}"
            Error $"Server key folder not found: {serverKeyPath.value}. Use ServerAdm to generate keys."
        else
            let keyFiles = Directory.GetFiles(serverKeyPath.value, "*.key")
            let pkxFiles = Directory.GetFiles(serverKeyPath.value, "*.pkx")

            if keyFiles.Length = 0 || pkxFiles.Length = 0 then
                Logger.logError $"Server keys not found in {serverKeyPath.value}"
                Error $"Server keys not found in {serverKeyPath.value}. Use ServerAdm to generate keys."
            else
                match tryImportPrivateKey (FileName keyFiles.[0]) None with
                | Ok (keyId, privateKey) ->
                    match tryImportPublicKey (FileName pkxFiles.[0]) (Some keyId) with
                    | Ok (_, publicKey) -> Ok (privateKey, publicKey)
                    | Error e ->
                        Logger.logError $"Failed to import server public key: %A{e}"
                        Error $"Failed to import server public key: %A{e}"
                | Error e ->
                    Logger.logError $"Failed to import server private key: %A{e}"
                    Error $"Failed to import server private key: %A{e}"


    let vpnServerMain programName argv =
        setLogLevel()
        let serverAccessInfo = loadVpnServerAccessInfo()

        Logger.logInfo $"vpnServerMain - serverAccessInfo = '{serverAccessInfo}'."

        match loadServerKeys serverAccessInfo.serverKeyPath with
        | Ok (privateKey, publicKey) ->
            let postBuildHandler _ _ =
                Logger.logInfo $"vpnServerMain - VPN Server started with subnet: {serverAccessInfo.vpnSubnet.value}"

            let saveSettings() =
                let result = updateVpnServerAccessInfo serverAccessInfo
                Logger.logInfo $"saveSettings - result: '%A{result}'."

            let projectName = getProjectName() |> Some

            let vpnServiceData =
                {
                    serverAccessInfo = serverAccessInfo
                    serverPrivateKey = privateKey
                    serverPublicKey = publicKey
                }

            let programData =
                {
                    serviceAccessInfo = serverAccessInfo.serviceAccessInfo
                    getService = fun () -> new VpnService(vpnServiceData) :> IVpnService
                    getWcfService = fun service -> new VpnWcfService(service)
                    saveSettings = saveSettings
                    configureServices = None
                    configureServiceLogging = configureServiceLogging projectName
                    configureLogging = configureLogging projectName
                    postBuildHandler = Some postBuildHandler
                }

            wcfMain<IVpnService, IVpnWcfService, VpnWcfService> programName programData argv

        | Error msg ->
            Logger.logCrit msg
            Softellect.Sys.ExitErrorCodes.CriticalError
