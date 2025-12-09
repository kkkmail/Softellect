namespace Softellect.Vpn.Core

open System
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.AppSettings
open Softellect.Wcf.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo

module AppSettings =

    let vpnServerAccessInfoKey = ConfigKey "VpnServerAccessInfo"
    let vpnClientAccessInfoKey = ConfigKey "VpnClientAccessInfo"
    let vpnSubnetKey = ConfigKey "VpnSubnet"
    let serverKeyPathKey = ConfigKey "ServerKeyPath"
    let clientKeysPathKey = ConfigKey "ClientKeysPath"
    let vpnClientIdKey = ConfigKey "VpnClientId"
    let clientKeyPathKey = ConfigKey "ClientKeyPath"
    let serverPublicKeyPathKey = ConfigKey "ServerPublicKeyPath"
    let localLanExclusionsKey = ConfigKey "LocalLanExclusions"
    let vpnClientsKey = ConfigKey "VpnClients"


    let private tryParseLocalLanExclusions (s: string) =
        s.Split([| ';' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun x -> LocalLanExclusion (x.Trim()))
        |> Array.toList


    let loadVpnServerAccessInfo () =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let d = VpnServerAccessInfo.defaultValue
            let serviceAccess = getServiceAccessInfo provider vpnServerAccessInfoKey d.serviceAccessInfo
            let vpnSubnet = provider.getStringOrDefault vpnSubnetKey d.vpnSubnet.value |> VpnSubnet
            let serverKeyPath = provider.getStringOrDefault serverKeyPathKey d.serverKeyPath.value |> FolderName
            let clientKeysPath = provider.getStringOrDefault clientKeysPathKey d.clientKeysPath.value |> FolderName

            {
                vpnDataVersion = VpnDataVersion.current
                serviceAccessInfo = serviceAccess
                vpnSubnet = vpnSubnet
                serverKeyPath = serverKeyPath
                clientKeysPath = clientKeysPath
            }
        | Error e ->
            Logger.logCrit $"loadVpnServerAccessInfo - Cannot load settings. Error: '%A{e}'."
            failwith $"loadVpnServerAccessInfo - Cannot load settings. Error: '%A{e}'."


    let loadVpnClientAccessInfo () =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let d = VpnClientAccessInfo.defaultValue
            let serverAccess = getServiceAccessInfo provider vpnClientAccessInfoKey d.serverAccessInfo

            let clientId =
                match provider.getStringOrDefault vpnClientIdKey "" |> VpnClientId.tryCreate with
                | Some id -> id
                | None ->
                    Logger.logWarn "loadVpnClientAccessInfo - No VpnClientId found in settings, generating new one."
                    VpnClientId.create()

            let clientKeyPath = provider.getStringOrDefault clientKeyPathKey d.clientKeyPath.value |> FolderName
            let serverPublicKeyPath = provider.getStringOrDefault serverPublicKeyPathKey d.serverPublicKeyPath.value |> FolderName

            let localLanExclusions =
                let s = provider.getStringOrDefault localLanExclusionsKey ""
                if String.IsNullOrWhiteSpace s then
                    LocalLanExclusion.defaultValues
                else
                    tryParseLocalLanExclusions s

            {
                vpnClientId = clientId
                serverAccessInfo = serverAccess
                clientKeyPath = clientKeyPath
                serverPublicKeyPath = serverPublicKeyPath
                localLanExclusions = localLanExclusions
            }
        | Error e ->
            Logger.logCrit $"loadVpnClientAccessInfo - Cannot load settings. Error: '%A{e}'."
            failwith $"loadVpnClientAccessInfo - Cannot load settings. Error: '%A{e}'."


    let updateVpnServerAccessInfo (info: VpnServerAccessInfo) =
        let toErr e = e |> ConfigErr |> Error

        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            provider.trySet vpnServerAccessInfoKey (info.serviceAccessInfo.serialize()) |> ignore
            provider.trySet vpnSubnetKey info.vpnSubnet.value |> ignore
            provider.trySet serverKeyPathKey info.serverKeyPath.value |> ignore
            provider.trySet clientKeysPathKey info.clientKeysPath.value |> ignore

            match provider.trySave() with
            | Ok () -> Ok ()
            | Error e -> toErr $"Failed to save settings: %A{e}"
        | Error e ->
            Logger.logError $"updateVpnServerAccessInfo: ERROR - %A{e}."
            toErr $"Failed to create settings provider: %A{e}"


    let updateVpnClientAccessInfo (info: VpnClientAccessInfo) =
        let toErr e = e |> ConfigErr |> Error

        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            provider.trySet vpnClientAccessInfoKey (info.serverAccessInfo.serialize()) |> ignore
            provider.trySet vpnClientIdKey (info.vpnClientId.value.ToString()) |> ignore
            provider.trySet clientKeyPathKey info.clientKeyPath.value |> ignore
            provider.trySet serverPublicKeyPathKey info.serverPublicKeyPath.value |> ignore

            let exclusionsStr =
                info.localLanExclusions
                |> List.map (fun x -> x.value)
                |> String.concat ";"

            provider.trySet localLanExclusionsKey exclusionsStr |> ignore

            match provider.trySave() with
            | Ok () -> Ok ()
            | Error e -> toErr $"Failed to save settings: %A{e}"
        | Error e ->
            Logger.logError $"updateVpnClientAccessInfo: ERROR - %A{e}."
            toErr $"Failed to create settings provider: %A{e}"
