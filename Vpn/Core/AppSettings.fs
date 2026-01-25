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

    type ConfigSection
        with
        static member vpnConnections = ConfigSection "vpnConnections"


    let getVpnTransportProtocol() = VpnTransportProtocol.UDP_Push

    /// TODO kk:20260118 - This needs to be consolidated with getEncryptionType1
    let getEncryptionType() = AES

    /// Returns (cleanupPeriod, maxIdle) for NAT cleanup.
    /// cleanupPeriod: how often to run cleanup
    /// maxIdle: how long a NAT mapping can be idle before removal
    let getNatCleanupSettings () : TimeSpan * TimeSpan =
        let cleanupPeriod = TimeSpan.FromSeconds(30.0)
        let maxIdle = TimeSpan.FromMinutes(30.0)
        (cleanupPeriod, maxIdle)


    let vpnServerAccessInfoKey = ConfigKey "VpnServerAccessInfo"
    let vpnConnectionNameKey = ConfigKey "VpnConnectionName"
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
    let adminAccessInfoKey = ConfigKey "AdminAccessInfo"
    let autoStartKey = ConfigKey "AutoStart"

    let defaultPhysicalGatewayIp = Ip4 "192.168.1.1"
    let defaultPhysicalInterfaceName = "Wi-Fi"


    type VpnClientAccessInfo
        with
        static member defaultValue =
            {
                vpnClientId = VpnClientId.create()
                vpnServerId = VpnServerId.create()
                vpnConnectionInfo =
                    {
                        vpnConnectionName  = VpnConnectionName.defaultValue
                        serverAccessInfo =
                            {
                                netTcpServiceAddress = ServiceAddress localHost
                                netTcpServicePort = ServicePort 5080
                                netTcpServiceName = ServiceName "VpnService"
                                netTcpSecurityMode = NoSecurity
                            }
                            |> NetTcpServiceInfo
                    }
                vpnConnections = []
                clientKeyPath = FolderName @"C:\Keys\VpnClient"
                serverPublicKeyPath = FolderName @"C:\Keys\VpnServer"
                localLanExclusions = LocalLanExclusion.defaultValues
                vpnTransportProtocol = getVpnTransportProtocol()
                physicalGatewayInfo =
                    {
                        gatewayIp = defaultPhysicalGatewayIp
                        interfaceName = defaultPhysicalInterfaceName
                    }
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


    let loadVpnConnections () =
        match AppSettingsProvider.tryCreate ConfigSection.vpnConnections with
        | Ok provider ->
            match provider.tryGetSectionKeys () with
            | Ok keys ->
                let d = VpnClientAccessInfo.defaultValue.vpnConnectionInfo.serverAccessInfo
                let connections = keys |> List.map (fun k -> { vpnConnectionName = VpnConnectionName k.value; serverAccessInfo = getServiceAccessInfo provider k d })
                Logger.logInfo $"Found {connections.Length} VPN connection points."
                connections
            | Error e ->
                Logger.logWarn $"Cannot load VPN connection keys: '%A{e}'."
                []
        | Error e ->
            Logger.logWarn $"Cannot load VPN connections: '%A{e}'."
            []


    let getClientId (provider : AppSettingsProvider) =
        match provider.getStringOrDefault vpnClientIdKey "" |> VpnClientId.tryCreate with
        | Some id -> id
        | None ->
            Logger.logWarn "No VpnClientId found in settings, generating new one."
            VpnClientId.create()


    let getVpnServerId (provider : AppSettingsProvider) =
        match provider.getStringOrDefault vpnServerIdKey "" |> VpnServerId.tryCreate with
        | Some id -> id
        | None ->
            Logger.logWarn "No VpnServerId found in settings, generating new one."
            VpnServerId.create()


    let getLocalLanExclusions (provider : AppSettingsProvider) =
        let s = provider.getStringOrDefault localLanExclusionsKey ""
        if String.IsNullOrWhiteSpace s then
            LocalLanExclusion.defaultValues
        else
            tryParseLocalLanExclusions s


    let getPhysicalGatewayIp (provider : AppSettingsProvider) =
        let s = provider.getStringOrDefault physicalGatewayIpKey ""
        if String.IsNullOrWhiteSpace s then
            defaultPhysicalGatewayIp
        else
            match IpAddress.tryCreate s with
            | Some ip -> ip
            | None ->
                Logger.logWarn $"loadVpnClientAccessInfo - Invalid PhysicalGatewayIp '{s}', using default."
                defaultPhysicalGatewayIp


    let getPhysicalInterfaceName (provider : AppSettingsProvider) =
        let s = provider.getStringOrDefault physicalInterfaceNameKey ""
        if String.IsNullOrWhiteSpace s then
            defaultPhysicalInterfaceName
        else
            s


    let getPhysicalGatewayInfo (provider : AppSettingsProvider) tryDetectPhysicalNetwork =
        match tryDetectPhysicalNetwork () with
        | Ok x -> x
        | Error e ->
            Logger.logError $"Physical gateway discovery failed: '%A{e}'. Resorting to appsettings.json data."

            {
                gatewayIp = getPhysicalGatewayIp provider
                interfaceName = getPhysicalInterfaceName provider
            }


    let getUseEncryption (provider : AppSettingsProvider) =
        let s = provider.getStringOrDefault useEncryptionKey ""
        s.ToLower() = "true"


    let getEncryptionType1 (provider : AppSettingsProvider) =
        let s = provider.getStringOrDefault encryptionTypeKey ""
        if String.IsNullOrWhiteSpace s then
            EncryptionType.defaultValue
        else
            EncryptionType.create s


    let loadVpnClientAccessInfo tryDetectPhysicalNetwork =
        let result =
            match AppSettingsProvider.tryCreate() with
            | Ok provider ->
                let d = VpnClientAccessInfo.defaultValue
                let vpnConnections = loadVpnConnections ()

                match vpnConnections with
                | [] -> Error "No VPN connections are available."
                | h :: _ ->
                    let vpnName = provider.getStringOrDefault vpnConnectionNameKey h.vpnConnectionName.value |> VpnConnectionName

                    let g c =
                        {
                            vpnClientId = getClientId provider
                            vpnServerId = getVpnServerId provider
                            vpnConnectionInfo = c
                            vpnConnections = vpnConnections
                            clientKeyPath = provider.getStringOrDefault clientKeyPathKey d.clientKeyPath.value |> FolderName
                            serverPublicKeyPath = provider.getStringOrDefault serverPublicKeyPathKey d.serverPublicKeyPath.value |> FolderName
                            localLanExclusions = getLocalLanExclusions provider
                            vpnTransportProtocol = getVpnTransportProtocol()
                            physicalGatewayInfo = getPhysicalGatewayInfo provider tryDetectPhysicalNetwork
                            useEncryption = getUseEncryption provider
                            encryptionType = getEncryptionType1 provider
                        }
                        |> Ok

                    match vpnConnections |> List.tryFind (fun e -> e.vpnConnectionName = vpnName) with
                    | Some connectionInfo -> g connectionInfo
                    | None ->
                        Logger.logWarn $"Specified VPN name: '{vpnName.value}' is invalid. Using a default value of '{h.vpnConnectionName.value}'."
                        g h
            | Error e -> Error $"Cannot load settings. Error: '%A{e}'."

        match result with
        | Ok r -> r
        | Error e ->
            Logger.logCrit e
            failwith e


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


    /// TODO kk:20260118 - Note that vpnClientAccessInfoKey was superseded by vpnConnectionNameKey
    let updateVpnClientAccessInfo (info: VpnClientAccessInfo) =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            // provider.trySet vpnClientAccessInfoKey (info.serverAccessInfo.serialize()) |> ignore
            provider.trySet vpnClientIdKey (info.vpnClientId.value.ToString()) |> ignore
            provider.trySet vpnServerIdKey (info.vpnServerId.value.ToString()) |> ignore
            provider.trySet clientKeyPathKey info.clientKeyPath.value |> ignore
            provider.trySet serverPublicKeyPathKey info.serverPublicKeyPath.value |> ignore

            let exclusionsStr =
                info.localLanExclusions
                |> List.map (fun x -> x.value)
                |> String.concat ";"

            provider.trySet localLanExclusionsKey exclusionsStr |> ignore
            provider.trySet physicalGatewayIpKey info.physicalGatewayInfo.gatewayIp.value |> ignore
            provider.trySet physicalInterfaceNameKey info.physicalGatewayInfo.interfaceName |> ignore

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


    /// Default admin service access info for localhost communication.
    let defaultAdminAccessInfo =
        {
            httpServiceAddress = ServiceAddress localHost
            httpServicePort = ServicePort 45002
            httpServiceName = ServiceName "VpnAdmService"
        }
        |> HttpServiceInfo


    /// Loads admin service access info from appsettings.json.
    let loadAdminAccessInfo () =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            getServiceAccessInfo provider adminAccessInfoKey defaultAdminAccessInfo
        | Error e ->
            Logger.logWarn $"loadAdminAccessInfo - Cannot load settings, using default. Error: '%A{e}'."
            defaultAdminAccessInfo


    /// Loads AutoStart setting from appsettings.json.
    /// Returns true if VPN should start automatically with the service.
    let loadAutoStart () =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let s = provider.getStringOrDefault autoStartKey ""
            s.ToLower() = "true"
        | Error e ->
            Logger.logWarn $"loadAutoStart - Cannot load settings, using default (false). Error: '%A{e}'."
            false


    /// Saves the selected VPN connection name to appsettings.json.
    let saveSelectedVpnConnectionName (name: VpnConnectionName) =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            match provider.trySet vpnConnectionNameKey name.value with
            | Ok () ->
                match provider.trySave() with
                | Ok () -> Ok ()
                | Error e -> toErr $"Failed to save settings: %A{e}"
            | Error e -> toErr $"Failed to set VPN connection name: %A{e}"
        | Error e ->
            Logger.logError $"saveSelectedVpnConnectionName: ERROR - %A{e}."
            toErr $"Failed to create settings provider: %A{e}"


    /// Saves the AutoStart setting to appsettings.json.
    let saveAutoStart (value: bool) =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let strValue = if value then "true" else "false"
            match provider.trySet autoStartKey strValue with
            | Ok () ->
                match provider.trySave() with
                | Ok () -> Ok ()
                | Error e -> toErr $"Failed to save settings: %A{e}"
            | Error e -> toErr $"Failed to set AutoStart: %A{e}"
        | Error e ->
            Logger.logError $"saveAutoStart: ERROR - %A{e}."
            toErr $"Failed to create settings provider: %A{e}"


    /// Loads the currently selected VPN connection name from appsettings.json.
    /// Returns None if not set, otherwise the configured name.
    let loadSelectedVpnConnectionName () =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let s = provider.getStringOrDefault vpnConnectionNameKey ""
            if String.IsNullOrWhiteSpace s then None
            else Some (VpnConnectionName s)
        | Error e ->
            Logger.logWarn $"loadSelectedVpnConnectionName - Cannot load settings. Error: '%A{e}'."
            None
