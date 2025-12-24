namespace Softellect.Vpn.Client

open System.IO
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.AppSettings
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.KeyManagement
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Client.Service
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

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

    let getClientService (serviceData : VpnClientServiceData) =
        match serviceData.clientAccessInfo.vpnTransportProtocol with
        | UDP_Push -> VpnPushClientService(serviceData) :> IHostedService


    let vpnClientMain programName argv =
        setLogLevel()
        let clientAccessInfo = loadVpnClientAccessInfo()

        Logger.logInfo $"vpnClientMain - clientAccessInfo.vpnClientId = '{clientAccessInfo.vpnClientId.value}'."

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

                let host =
                    Host.CreateDefaultBuilder()
                        .UseWindowsService()
                        .ConfigureLogging(fun logging ->
                            match isService() with
                            | true -> configureServiceLogging (Some (getProjectName())) logging
                            | false -> configureLogging (Some (getProjectName())) logging)
                        .ConfigureServices(fun hostContext services ->
                            let service = getClientService serviceData
                            // services.AddSingleton<IHostedService>(service) |> ignore
                            services.AddSingleton<IHostedService>(service :> IHostedService) |> ignore)
                        .Build()

                host.Run()
                Softellect.Sys.ExitErrorCodes.CompletedSuccessfully

            | Error msg ->
                Logger.logCrit msg
                Softellect.Sys.ExitErrorCodes.CriticalError

        | Error msg ->
            Logger.logCrit msg
            Softellect.Sys.ExitErrorCodes.CriticalError
