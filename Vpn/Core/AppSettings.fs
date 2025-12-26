namespace Softellect.Vpn.Core

open System
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.AppSettings
open Softellect.Wcf.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Wcf.Common

module AppSettings =
    let getVpnTransportProtocol() = VpnTransportProtocol.UDP_Push
    let getEncryptionType() = AES

    /// Returns (cleanupPeriod, maxIdle) for NAT cleanup.
    /// cleanupPeriod: how often to run cleanup
    /// maxIdle: how long a NAT mapping can be idle before removal
    let getNatCleanupSettings () : TimeSpan * TimeSpan =
        let cleanupPeriod = TimeSpan.FromSeconds(30.0)
        let maxIdle = TimeSpan.FromMinutes(30.0)
        (cleanupPeriod, maxIdle)


    let vpnServerAccessInfoKey = ConfigKey "VpnServerAccessInfo"
    let vpnClientAccessInfoKey = ConfigKey "VpnClientAccessInfo"
    let vpnServerIdKey = ConfigKey "VpnServerId"
    let vpnSubnetKey = ConfigKey "VpnSubnet"
    let serverKeyPathKey = ConfigKey "ServerKeyPath"
    let clientKeysPathKey = ConfigKey "ClientKeysPath"
    let vpnClientIdKey = ConfigKey "VpnClientId"
    let clientKeyPathKey = ConfigKey "ClientKeyPath"
    let serverPublicKeyPathKey = ConfigKey "ServerPublicKeyPath"
    let localLanExclusionsKey = ConfigKey "LocalLanExclusions"
    let physicalGatewayIpKey = ConfigKey "PhysicalGatewayIp"
    let physicalInterfaceNameKey = ConfigKey "PhysicalInterfaceName"
    let useEncryptionKey = ConfigKey "UseEncryption"
    let encryptionTypeKey = ConfigKey "EncryptionType"

    let defaultPhysicalGatewayIp = Ip4 "192.168.1.1"
    let defaultPhysicalInterfaceName = "Wi-Fi"


    type VpnClientAccessInfo
        with
        static member defaultValue =
            {
                vpnClientId = VpnClientId.create()
                vpnServerId = VpnServerId.create()
                serverAccessInfo =
                    {
                        netTcpServiceAddress = ServiceAddress localHost
                        netTcpServicePort = ServicePort 5080
                        netTcpServiceName = ServiceName "VpnService"
                        netTcpSecurityMode = NoSecurity
                    }
                    |> NetTcpServiceInfo
                clientKeyPath = FolderName @"C:\Keys\VpnClient"
                serverPublicKeyPath = FolderName @"C:\Keys\VpnServer"
                localLanExclusions = LocalLanExclusion.defaultValues
                vpnTransportProtocol = getVpnTransportProtocol()
                physicalGatewayIp = defaultPhysicalGatewayIp
                physicalInterfaceName = defaultPhysicalInterfaceName
                useEncryption = false
                encryptionType = EncryptionType.defaultValue
            }


    type VpnServerAccessInfo
        with

        static member defaultValue =
            {
                vpnDataVersion = VpnDataVersion.current
                vpnServerId = VpnServerId.create()
                serviceAccessInfo =
                    {
                        netTcpServiceAddress = ServiceAddress localHost
                        netTcpServicePort = ServicePort 5080
                        netTcpServiceName = ServiceName "VpnService"
                        netTcpSecurityMode = NoSecurity
                    }
                    |> NetTcpServiceInfo
                vpnSubnet = VpnSubnet.defaultValue
                serverKeyPath = FolderName @"C:\Keys\VpnServer"
                clientKeysPath = FolderName @"C:\Keys\VpnClient"
                vpnTransportProtocol = getVpnTransportProtocol()
                encryptionType = getEncryptionType()
            }


    let private tryParseLocalLanExclusions (s: string) =
        s.Split([| ';' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun x -> LocalLanExclusion (x.Trim()))
        |> Array.toList


    let private toErr e = e |> ConfigErr |> Error


    type VpnClientData
        with
        static member private marker = Unchecked.defaultof<VpnClientData>

        static member tryDeserialize (s : string) =
            let p = parseSimpleSetting s

            match p |> Map.tryFind (nameof VpnClientData.marker.clientName), p |> Map.tryFind (nameof VpnClientData.marker.assignedIp) with
            | Some a, Some b ->
                match b |> VpnIpAddress.tryCreate with
                | Some v ->
                    let useEncryption =
                        match p |> Map.tryFind (nameof VpnClientData.marker.useEncryption) with
                        | Some s -> s.ToLower() = "true"
                        | None -> false

                    let encryptionType =
                        match p |> Map.tryFind (nameof VpnClientData.marker.encryptionType) with
                        | Some s -> EncryptionType.create s
                        | None -> EncryptionType.defaultValue

                    {
                        clientName = VpnClientName a
                        assignedIp = v
                        vpnTransportProtocol = getVpnTransportProtocol()
                        useEncryption = useEncryption
                        encryptionType = encryptionType
                    }
                    |> Ok
                | None -> $"Some values in '{s}' are invalid." |> ConfigErr |> Error
            | _ -> $"Value of '{s}' is invalid." |> ConfigErr |> Error


    let loadVpnServerAccessInfo () =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let d = VpnServerAccessInfo.defaultValue
            let serviceAccess = getServiceAccessInfo provider vpnServerAccessInfoKey d.serviceAccessInfo

            let vpnServerId =
                match provider.getStringOrDefault vpnServerIdKey "" |> VpnServerId.tryCreate with
                | Some id -> id
                | None ->
                    Logger.logWarn "loadVpnServerAccessInfo - No VpnServerId found in settings, generating new one."
                    VpnServerId.create()

            let vpnSubnet = provider.getStringOrDefault vpnSubnetKey d.vpnSubnet.value |> VpnSubnet
            let serverKeyPath = provider.getStringOrDefault serverKeyPathKey d.serverKeyPath.value |> FolderName
            let clientKeysPath = provider.getStringOrDefault clientKeysPathKey d.clientKeysPath.value |> FolderName

            {
                vpnDataVersion = VpnDataVersion.current
                vpnServerId = vpnServerId
                serviceAccessInfo = serviceAccess
                vpnSubnet = vpnSubnet
                serverKeyPath = serverKeyPath
                clientKeysPath = clientKeysPath
                vpnTransportProtocol = getVpnTransportProtocol()
                encryptionType = getEncryptionType()
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

            let vpnServerId =
                match provider.getStringOrDefault vpnServerIdKey "" |> VpnServerId.tryCreate with
                | Some id -> id
                | None ->
                    Logger.logWarn "loadVpnClientAccessInfo - No VpnServerId found in settings, generating new one."
                    VpnServerId.create()

            let clientKeyPath = provider.getStringOrDefault clientKeyPathKey d.clientKeyPath.value |> FolderName
            let serverPublicKeyPath = provider.getStringOrDefault serverPublicKeyPathKey d.serverPublicKeyPath.value |> FolderName

            let localLanExclusions =
                let s = provider.getStringOrDefault localLanExclusionsKey ""
                if String.IsNullOrWhiteSpace s then
                    LocalLanExclusion.defaultValues
                else
                    tryParseLocalLanExclusions s

            let physicalGatewayIp =
                let s = provider.getStringOrDefault physicalGatewayIpKey ""
                if String.IsNullOrWhiteSpace s then
                    defaultPhysicalGatewayIp
                else
                    match IpAddress.tryCreate s with
                    | Some ip -> ip
                    | None ->
                        Logger.logWarn $"loadVpnClientAccessInfo - Invalid PhysicalGatewayIp '{s}', using default."
                        defaultPhysicalGatewayIp

            let physicalInterfaceName =
                let s = provider.getStringOrDefault physicalInterfaceNameKey ""
                if String.IsNullOrWhiteSpace s then
                    defaultPhysicalInterfaceName
                else
                    s

            let useEncryption =
                let s = provider.getStringOrDefault useEncryptionKey ""
                s.ToLower() = "true"

            let encryptionType =
                let s = provider.getStringOrDefault encryptionTypeKey ""
                if String.IsNullOrWhiteSpace s then
                    EncryptionType.defaultValue
                else
                    EncryptionType.create s

            {
                vpnClientId = clientId
                vpnServerId = vpnServerId
                serverAccessInfo = serverAccess
                clientKeyPath = clientKeyPath
                serverPublicKeyPath = serverPublicKeyPath
                localLanExclusions = localLanExclusions
                vpnTransportProtocol = getVpnTransportProtocol()
                physicalGatewayIp = physicalGatewayIp
                physicalInterfaceName = physicalInterfaceName
                useEncryption = useEncryption
                encryptionType = encryptionType
            }
        | Error e ->
            Logger.logCrit $"loadVpnClientAccessInfo - Cannot load settings. Error: '%A{e}'."
            failwith $"loadVpnClientAccessInfo - Cannot load settings. Error: '%A{e}'."


    let updateVpnServerAccessInfo (info: VpnServerAccessInfo) =

        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            provider.trySet vpnServerAccessInfoKey (info.serviceAccessInfo.serialize()) |> ignore
            provider.trySet vpnServerIdKey (info.vpnServerId.value.ToString()) |> ignore
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
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            provider.trySet vpnClientAccessInfoKey (info.serverAccessInfo.serialize()) |> ignore
            provider.trySet vpnClientIdKey (info.vpnClientId.value.ToString()) |> ignore
            provider.trySet vpnServerIdKey (info.vpnServerId.value.ToString()) |> ignore
            provider.trySet clientKeyPathKey info.clientKeyPath.value |> ignore
            provider.trySet serverPublicKeyPathKey info.serverPublicKeyPath.value |> ignore

            let exclusionsStr =
                info.localLanExclusions
                |> List.map (fun x -> x.value)
                |> String.concat ";"

            provider.trySet localLanExclusionsKey exclusionsStr |> ignore
            provider.trySet physicalGatewayIpKey info.physicalGatewayIp.value |> ignore
            provider.trySet physicalInterfaceNameKey info.physicalInterfaceName |> ignore

            match provider.trySave() with
            | Ok () -> Ok ()
            | Error e -> toErr $"Failed to save settings: %A{e}"
        | Error e ->
            Logger.logError $"updateVpnClientAccessInfo: ERROR - %A{e}."
            toErr $"Failed to create settings provider: %A{e}"


    let tryWritePhysicalNetworkConfig (gatewayIp: IpAddress) (interfaceName: string) =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            provider.trySet physicalGatewayIpKey gatewayIp.value |> ignore
            provider.trySet physicalInterfaceNameKey interfaceName |> ignore

            match provider.trySave() with
            | Ok () -> Ok ()
            | Error e -> toErr $"Failed to save settings: %A{e}"
        | Error e ->
            Logger.logError $"tryWritePhysicalNetworkConfig: ERROR - %A{e}."
            toErr $"Failed to create settings provider: %A{e}"


    // let loadVpnClientConfigs () : VpnClientConfig list =
    //     match AppSettingsProvider.tryCreate() with
    //     | Ok provider ->
    //         match provider.tryGetNestedSection ["appSettings"; "VpnClients"] with
    //         | Ok (Some clientsObj) ->
    //             let configs =
    //                 clientsObj.Properties()
    //                 |> Seq.choose (fun prop ->
    //                     let clientName = prop.Name
    //                     match provider.tryGetNested ["appSettings"; "VpnClients"; clientName] "ClientId" with
    //                     | Ok (Some clientIdStr) ->
    //                         match provider.tryGetNested ["appSettings"; "VpnClients"; clientName] "AssignedIp" with
    //                         | Ok (Some assignedIpStr) ->
    //                             match VpnClientId.tryCreate clientIdStr, VpnIpAddress.tryCreate assignedIpStr with
    //                             | Some clientId, Some assignedIp ->
    //                                 Some {
    //                                     clientId = clientId
    //                                     clientName = VpnClientName clientName
    //                                     assignedIp = assignedIp
    //                                 }
    //                             | _ -> None
    //                         | _ -> None
    //                     | _ -> None
    //                 )
    //                 |> Seq.toList
    //
    //             Logger.logInfo $"loadVpnClientConfigs: Loaded {configs.Length} client(s)"
    //             configs
    //         | Ok None ->
    //             Logger.logInfo "loadVpnClientConfigs: No VpnClients section found"
    //             []
    //         | Error e ->
    //             Logger.logError $"loadVpnClientConfigs: ERROR getting VpnClients section - %A{e}."
    //             []
    //     | Error e ->
    //         Logger.logError $"loadVpnClientConfigs: ERROR creating provider - %A{e}."
    //         []
    //
    //
    // let addOrUpdateVpnClientConfig (config: VpnClientConfig) =
    //     let toErr e = e |> ConfigErr |> Error
    //
    //     match AppSettingsProvider.tryCreate() with
    //     | Ok provider ->
    //         let clientName = config.clientName.value
    //         let sections = ["appSettings"; "VpnClients"; clientName]
    //
    //         match provider.trySetNested sections "ClientId" (config.clientId.value.ToString()) with
    //         | Ok () ->
    //             match provider.trySetNested sections "AssignedIp" config.assignedIp.value with
    //             | Ok () ->
    //                 Logger.logInfo $"addOrUpdateVpnClientConfig: Set values for client {clientName}"
    //
    //                 match provider.trySave() with
    //                 | Ok () ->
    //                     Logger.logInfo "addOrUpdateVpnClientConfig: trySave succeeded"
    //                     Ok ()
    //                 | Error e ->
    //                     Logger.logError $"addOrUpdateVpnClientConfig: trySave failed: %A{e}"
    //                     toErr $"Failed to save client config: %A{e}"
    //             | Error e ->
    //                 Logger.logError $"addOrUpdateVpnClientConfig: trySetNested AssignedIp failed: %A{e}"
    //                 toErr $"Failed to set AssignedIp: %A{e}"
    //         | Error e ->
    //             Logger.logError $"addOrUpdateVpnClientConfig: trySetNested ClientId failed: %A{e}"
    //             toErr $"Failed to set ClientId: %A{e}"
    //     | Error e ->
    //         Logger.logError $"addOrUpdateVpnClientConfig: ERROR creating provider - %A{e}."
    //         toErr $"Failed to create settings provider: %A{e}"

    let addOrUpdateVpnClientConfig (config: VpnClientConfig) =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let clientKey = config.clientId.configKey
            let data = config.clientData.serialize()

            match provider.trySet clientKey data with
            | Ok () -> Logger.logTrace (fun () -> $"Client: '{clientKey.value}' = '{data}' was set successfully.")
            | Error e -> Logger.logError $"Client: '{clientKey.value}', error '%A{e}'."

            match provider.trySave() with
            | Ok () -> Ok ()
            | Error e -> toErr $"Failed to save settings: '%A{e}'."
        | Error e ->
            Logger.logError $"addOrUpdateVpnClientConfig: ERROR creating provider - '%A{e}'."
            toErr $"Failed to create settings provider: '%A{e}'."


    let tryLoadVpnClientConfig (clientId : VpnClientId ) =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let clientKey = clientId.configKey
            let tryCreate s =
                match VpnClientData.tryDeserialize s with
                | Ok s -> Ok s
                | Error e -> Error $"'%A{e}'."

            match provider.tryGet tryCreate clientKey  with
            | Ok ro ->
                match ro with
                | Some r ->
                    Logger.logTrace (fun () -> $"Successfully loaded client data: '%A{r}'.")
                    Ok r
                | None ->
                    Logger.logWarn $"Cannot load client config for client: '{clientId.value}'."
                    toErr $"Cannot load client config for client: '{clientId.value}'."
            | Error e ->
                Logger.logError $"Client: '{clientKey.value}', error '%A{e}'."
                toErr $"Failed to parse client data: '%A{e}'."
        | Error e ->
            Logger.logError $"addOrUpdateVpnClientConfig: ERROR creating provider - '%A{e}'."
            toErr $"Failed to create settings provider: %A{e}"
