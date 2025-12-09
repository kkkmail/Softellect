namespace Softellect.Vpn.Client

open System.IO
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.AppSettings
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Client.Service
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

module Program =

    let private loadClientKeys (clientKeyPath: FolderName) =
        let privateKeyFile = Path.Combine(clientKeyPath.value, "client.key")
        let publicKeyFile = Path.Combine(clientKeyPath.value, "client.pkx")

        if File.Exists(privateKeyFile) && File.Exists(publicKeyFile) then
            try
                let privateKeyXml = File.ReadAllText(privateKeyFile)
                let publicKeyXml = File.ReadAllText(publicKeyFile)
                Ok (PrivateKey privateKeyXml, PublicKey publicKeyXml)
            with
            | ex ->
                Logger.logError $"Failed to load client keys: {ex.Message}"
                Error $"Failed to load client keys: {ex.Message}"
        else
            Logger.logError $"Client keys not found in {clientKeyPath.value}"
            Error $"Client keys not found in {clientKeyPath.value}. Use ClientAdm to generate keys."


    let private loadServerPublicKey (serverPublicKeyPath: FileName) =
        if File.Exists(serverPublicKeyPath.value) then
            try
                match tryImportPublicKey serverPublicKeyPath None with
                | Ok (_, publicKey) -> Ok publicKey
                | Error e ->
                    Logger.logError $"Failed to import server public key: %A{e}"
                    Error $"Failed to import server public key: %A{e}"
            with
            | ex ->
                Logger.logError $"Failed to load server public key: {ex.Message}"
                Error $"Failed to load server public key: {ex.Message}"
        else
            Logger.logError $"Server public key not found: {serverPublicKeyPath.value}"
            Error $"Server public key not found: {serverPublicKeyPath.value}"


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
