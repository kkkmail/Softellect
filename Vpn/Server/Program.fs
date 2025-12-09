namespace Softellect.Vpn.Server

open System
open System.IO
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.AppSettings
open Softellect.Wcf.Program
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Server.Service

module Program =

    let private loadServerKeys (serverKeyPath: FolderName) =
        let privateKeyFile = Path.Combine(serverKeyPath.value, "server.key")
        let publicKeyFile = Path.Combine(serverKeyPath.value, "server.pkx")

        if File.Exists(privateKeyFile) && File.Exists(publicKeyFile) then
            try
                let privateKeyXml = File.ReadAllText(privateKeyFile)
                let publicKeyXml = File.ReadAllText(publicKeyFile)
                Ok (PrivateKey privateKeyXml, PublicKey publicKeyXml)
            with
            | ex ->
                Logger.logError $"Failed to load server keys: {ex.Message}"
                Error $"Failed to load server keys: {ex.Message}"
        else
            Logger.logError $"Server keys not found in {serverKeyPath.value}"
            Error $"Server keys not found in {serverKeyPath.value}. Use ServerAdm to generate keys."


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
