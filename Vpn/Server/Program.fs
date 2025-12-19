namespace Softellect.Vpn.Server

open System.IO
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Server.WcfServer
open Softellect.Vpn.Server.UdpServer
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
                match tryImportPrivateKey (FileName keyFiles[0]) None with
                | Ok (keyId, privateKey) ->
                    match tryImportPublicKey (FileName pkxFiles[0]) (Some keyId) with
                    | Ok (_, publicKey) -> Ok (privateKey, publicKey)
                    | Error e ->
                        Logger.logError $"Failed to import server public key: %A{e}"
                        Error $"Failed to import server public key: %A{e}"
                | Error e ->
                    Logger.logError $"Failed to import server private key: %A{e}"
                    Error $"Failed to import server private key: %A{e}"


    let getProgram (data : VpnServerData) argv =
        let getService = fun () -> VpnService(data) :> IVpnService

        match data.serverAccessInfo.vpnTransportProtocol with
        | WCF_Tunnel -> getWcfProgram data getService argv
        | UDP_Tunnel -> getUdpProgram data getService argv


    let vpnServerMain argv =
        setLogLevel()
        let serverAccessInfo = loadVpnServerAccessInfo()

        Logger.logInfo $"vpnServerMain - serverAccessInfo = '{serverAccessInfo}'."

        match loadServerKeys serverAccessInfo.serverKeyPath with
        | Ok (privateKey, publicKey) ->
            let data =
                {
                    serverAccessInfo = serverAccessInfo
                    serverPrivateKey = privateKey
                    serverPublicKey = publicKey
                }

            let program = getProgram data argv
            program()
        | Error msg ->
            Logger.logCrit msg
            Softellect.Sys.ExitErrorCodes.CriticalError
