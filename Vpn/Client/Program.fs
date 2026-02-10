namespace Softellect.Vpn.Client

open System.IO
open System.Threading
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.AppSettings
open Softellect.Vpn.Client.NetworkDetector
open Softellect.Wcf.Program
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.KeyManagement
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Client.Service
open Softellect.Vpn.Client.AdminService

module Program =

    let private loadClientKeys (clientKeyPath: FolderName) (clientId : VpnClientId) =
        if not (Directory.Exists clientKeyPath.value) then
            Logger.logError $"Client key folder not found: {clientKeyPath.value}"
            Error $"Client key folder not found: {clientKeyPath.value}. Use ClientAdm to generate keys."
        else
            let keyFilePath = (FileName $"{clientId.value}.key").combine clientKeyPath
            let pkxFilePath = (FileName $"{clientId.value}.pkx").combine clientKeyPath

            match tryImportPrivateKey keyFilePath None with
            | Ok (keyId, privateKey) ->
                match tryImportPublicKey pkxFilePath (Some keyId) with
                | Ok (_, publicKey) -> Ok (privateKey, publicKey)
                | Error e ->
                    Logger.logError $"Failed to import client public key: %A{e}"
                    Error $"Failed to import client public key: %A{e}"
            | Error e ->
                Logger.logError $"Failed to import client private key: %A{e}"
                Error $"Failed to import client private key: %A{e}"


    let private loadServerPublicKey (serverPublicKeyPath: FolderName) (vpnServerId: VpnServerId) =
        if not (Directory.Exists serverPublicKeyPath.value) then
            Logger.logError $"Server public key folder not found: {serverPublicKeyPath.value}"
            Error $"Server public key folder not found: {serverPublicKeyPath.value}"
        else
            let keyId = KeyId vpnServerId.value
            let pkxFileName = FileName $"{vpnServerId.value}.pkx"
            let pkxFilePath = pkxFileName.combine serverPublicKeyPath

            if not (File.Exists pkxFilePath.value) then
                Logger.logError $"Server public key file not found: {pkxFilePath.value}"
                Error $"Server public key file not found: {pkxFilePath.value}"
            else
                match tryImportPublicKey pkxFilePath (Some keyId) with
                | Ok (_, publicKey) -> Ok publicKey
                | Error e ->
                    Logger.logError $"Failed to import server public key: %A{e}"
                    Error $"Failed to import server public key: %A{e}"

    let private waitForNetworkConnection () =
        let minDelayMs = 30_000
        let maxDelayMs = 300_000

        let rec wait delayMs =
            match tryDetectPhysicalNetwork () with
            | Ok networkInfo ->
                Logger.logInfo $"waitForNetworkConnection: physical network detected - gateway = '{networkInfo.gatewayIp}', interface = '{networkInfo.interfaceName}'."
            | Error e ->
                Logger.logWarn $"waitForNetworkConnection: physical network not detected: '{e}'. Retrying in {delayMs / 1000} seconds..."
                Thread.Sleep(delayMs)
                let newDelayMs = min (delayMs + minDelayMs) maxDelayMs
                wait newDelayMs

        wait minDelayMs


    let getClientService (serviceData : VpnClientServiceData) (autoStart: bool) =
        match serviceData.clientAccessInfo.vpnTransportProtocol with
        | UDP_Push -> VpnPushClientService(serviceData, autoStart)


    let vpnClientMain programName argv =
        setLogLevel()
        let clientAccessInfo = loadVpnClientAccessInfo tryDetectPhysicalNetwork
        let adminAccessInfo = loadAdminAccessInfo()
        let autoStart = loadAutoStart() || (not (isService())) // If we run as EXE, then connect to VPN.

        Logger.logInfo $"vpnClientMain - clientAccessInfo.vpnClientId = '{clientAccessInfo.vpnClientId.value}', autoStart = {autoStart}."
        Logger.logInfo $"vpnClientMain - adminAccessInfo = '{adminAccessInfo}'."

        match loadClientKeys clientAccessInfo.clientKeyPath clientAccessInfo.vpnClientId with
        | Ok (clientPrivateKey, clientPublicKey) ->
            match loadServerPublicKey clientAccessInfo.serverPublicKeyPath clientAccessInfo.vpnServerId with
            | Ok serverPublicKey ->
                let serviceData =
                    {
                        clientAccessInfo = clientAccessInfo
                        clientPrivateKey = clientPrivateKey
                        clientPublicKey = clientPublicKey
                        serverPublicKey = serverPublicKey
                    }

                // Wait for physical network to be available before starting VPN.
                waitForNetworkConnection ()

                // Create the VPN client service (implements both IHostedService and IAdminService)
                let vpnService = getClientService serviceData autoStart

                // Create the admin WCF service wrapper
                let adminWcfService = AdminWcfService(vpnService :> IAdminService)

                let projectName = getProjectName() |> Some

                let configureServices (services : IServiceCollection) =
                    services.AddSingleton<IHostedService>(vpnService :> IHostedService) |> ignore

                let programData =
                    {
                        serviceAccessInfo = adminAccessInfo
                        getService = fun () -> vpnService :> IAdminService
                        getWcfService = fun _ -> adminWcfService
                        saveSettings = fun () -> ()
                        configureServices = Some configureServices
                        configureServiceLogging = configureServiceLogging projectName
                        configureLogging = configureLogging projectName
                        postBuildHandler = None
                    }

                wcfMain<IAdminService, IAdminWcfService, AdminWcfService> programName programData argv
            | Error msg ->
                Logger.logCrit msg
                Softellect.Sys.ExitErrorCodes.CriticalError
        | Error msg ->
            Logger.logCrit msg
            Softellect.Sys.ExitErrorCodes.CriticalError
