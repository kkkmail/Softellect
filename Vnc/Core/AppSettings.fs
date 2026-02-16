namespace Softellect.Vnc.Core

open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.AppSettings
open Softellect.Wcf.AppSettings
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.ServiceInfo

module AppSettings =

    type ConfigSection
        with
        static member vncMachines = ConfigSection "vncMachines"


    let vncMachineNameKey = ConfigKey "VncMachineName"
    let vncMachineIdKey = ConfigKey "VncMachineId"
    let vncRepeaterAccessInfoKey = ConfigKey "VncRepeaterAccessInfo"
    let vncServiceAccessInfoKey = ConfigKey "VncServiceAccessInfo"
    let vncUdpPortKey = ConfigKey "VncUdpPort"
    let vncServerKeyPathKey = ConfigKey "VncServerKeyPath"
    let vncViewerKeysPathKey = ConfigKey "VncViewerKeysPath"
    let vncViewerIdKey = ConfigKey "VncViewerId"
    let vncViewerKeyPathKey = ConfigKey "VncViewerKeyPath"
    let vncServerPublicKeyPathKey = ConfigKey "VncServerPublicKeyPath"


    type VncMachineConfig =
        {
            machineName : VncMachineName
            machineId : VncMachineId
        }


    let loadVncMachines () : VncMachineConfig list =
        match AppSettingsProvider.tryCreate ConfigSection.vncMachines with
        | Ok provider ->
            match provider.tryGetSectionKeys () with
            | Ok keys ->
                keys
                |> List.choose (fun k ->
                    let name = provider.getStringOrDefault vncMachineNameKey ""
                    let idStr = provider.getStringOrDefault vncMachineIdKey ""
                    if name <> "" && idStr <> "" then
                        match VncMachineId.tryCreate idStr with
                        | Some machineId ->
                            Some
                                {
                                    machineName = VncMachineName name
                                    machineId = machineId
                                }
                        | None ->
                            Logger.logWarn $"loadVncMachines: Invalid machineId '{idStr}' for key '{k}'."
                            None
                    else
                        Logger.logWarn $"loadVncMachines: Missing name or id for key '{k}'."
                        None)
            | Error e ->
                Logger.logWarn $"loadVncMachines: Failed to get section keys: %A{e}"
                []
        | Error e ->
            Logger.logWarn $"loadVncMachines: Failed to create provider: %A{e}"
            []


    let loadVncServiceAccessInfo () : VncServiceAccessInfo =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let d = VncServiceAccessInfo.defaultValue
            let serviceAccess = getServiceAccessInfo provider vncServiceAccessInfoKey d.serviceAccessInfo
            let udpPort = provider.getIntOrDefault vncUdpPortKey d.udpPort

            {
                serviceAccessInfo = serviceAccess
                udpPort = udpPort
            }
        | Error e ->
            Logger.logCrit $"loadVncServiceAccessInfo - Cannot load settings. Error: '%A{e}'."
            failwith $"loadVncServiceAccessInfo - Cannot load settings. Error: '%A{e}'."


    let loadVncServerKeyPath () : FolderName =
        match AppSettingsProvider.tryCreate() with
        | Ok provider -> provider.getStringOrDefault vncServerKeyPathKey "Keys/Server" |> FolderName
        | Error _ -> FolderName "Keys/Server"


    let loadVncViewerKeysPath () : FolderName =
        match AppSettingsProvider.tryCreate() with
        | Ok provider -> provider.getStringOrDefault vncViewerKeysPathKey "Keys/Viewers" |> FolderName
        | Error _ -> FolderName "Keys/Viewers"
