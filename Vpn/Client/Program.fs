namespace Softellect.Vpn.Client

open System.IO
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.AppSettings
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.KeyManagement
open Softellect.Vpn.Client.Service
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

module Program =

    let private loadClientKeys (clientKeyPath: FolderName) =
        if not (Directory.Exists clientKeyPath.value) then
            Logger.logError $"Client key folder not found: {clientKeyPath.value}"
            Error $"Client key folder not found: {clientKeyPath.value}. Use ClientAdm to generate keys."
        else
            let keyFiles = Directory.GetFiles(clientKeyPath.value, "*.key")
            let pkxFiles = Directory.GetFiles(clientKeyPath.value, "*.pkx")

            if keyFiles.Length = 0 || pkxFiles.Length = 0 then
                Logger.logError $"Client keys not found in {clientKeyPath.value}"
                Error $"Client keys not found in {clientKeyPath.value}. Use ClientAdm to generate keys."
            else
                match tryImportPrivateKey (FileName keyFiles.[0]) None with
                | Ok (keyId, privateKey) ->
                    match tryImportPublicKey (FileName pkxFiles.[0]) (Some keyId) with
                    | Ok (_, publicKey) -> Ok (privateKey, publicKey)
                    | Error e ->
                        Logger.logError $"Failed to import client public key: %A{e}"
                        Error $"Failed to import client public key: %A{e}"
                | Error e ->
                    Logger.logError $"Failed to import client private key: %A{e}"
                    Error $"Failed to import client private key: %A{e}"


    let private loadServerPublicKey (serverPublicKeyPath: FolderName) =
        if not (Directory.Exists serverPublicKeyPath.value) then
            Logger.logError $"Server public key folder not found: {serverPublicKeyPath.value}"
            Error $"Server public key folder not found: {serverPublicKeyPath.value}"
        else
            let pkxFiles = Directory.GetFiles(serverPublicKeyPath.value, "*.pkx")

            if pkxFiles.Length = 0 then
                Logger.logError $"Server public key not found in: {serverPublicKeyPath.value}"
                Error $"Server public key not found in: {serverPublicKeyPath.value}"
            else
                match tryImportPublicKey (FileName pkxFiles.[0]) None with
                | Ok (_, publicKey) -> Ok publicKey
                | Error e ->
                    Logger.logError $"Failed to import server public key: %A{e}"
                    Error $"Failed to import server public key: %A{e}"


    let vpnClientMain programName argv =
        setLogLevel()
        let clientAccessInfo = loadVpnClientAccessInfo()

        Logger.logInfo $"vpnClientMain - clientAccessInfo.vpnClientId = '{clientAccessInfo.vpnClientId.value}'."

        match loadClientKeys clientAccessInfo.clientKeyPath with
        | Ok (clientPrivateKey, clientPublicKey) ->
            match loadServerPublicKey clientAccessInfo.serverPublicKeyPath with
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
                            let service = new VpnClientService(serviceData)
                            services.AddSingleton<VpnClientService>(service) |> ignore
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
