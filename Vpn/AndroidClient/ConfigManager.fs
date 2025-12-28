namespace Softellect.Vpn.AndroidClient

open System
open System.IO
open Newtonsoft.Json
open Softellect.Sys.Primitives
open Softellect.Sys.Logging
open Softellect.Wcf.Common
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo

module ConfigManager =

    /// JSON config structure for Android VPN client.
    /// Must include all fields needed to construct VpnClientServiceData.
    [<CLIMutable>]
    type VpnClientConfig =
        {
            /// Server hostname or IP address.
            serverHost : string

            /// BasicHttp port for WCF auth/ping.
            basicHttpPort : int

            /// UDP port for data plane.
            udpPort : int

            /// Client ID (GUID string).
            clientId : string

            ///// Server ID (GUID string).
            //serverId : string

            /// Client private key (base64 encoded).
            clientPrivateKey : string

            /// Client public key (base64 encoded).
            clientPublicKey : string

            /// Server public key (base64 encoded).
            serverPublicKey : string

            /// Whether to use encryption (default true).
            useEncryption : bool

            /// Encryption type (default "AES").
            encryptionType : string
        }

        static member defaultValue =
            {
                serverHost = ""
                basicHttpPort = 0
                udpPort = 0
                clientId = ""
                //serverId = ""
                clientPrivateKey = ""
                clientPublicKey = ""
                serverPublicKey = ""
                useEncryption = true
                encryptionType = "AES"
            }


    /// Parse encryption type from string.
    let parseEncryptionType (s: string) : EncryptionType =
        match s.ToLowerInvariant() with
        | "aes256" | "aes" -> AES
        | "rsa" -> RSA
        | _ -> AES // Default to AES256


    /// Load config from JSON string.
    let tryLoadConfigFromJson (json: string) : Result<VpnClientConfig, string> =
        try
            let config = JsonConvert.DeserializeObject<VpnClientConfig>(json)
            if String.IsNullOrWhiteSpace(config.serverHost) then
                Error "serverHost is required"
            elif config.basicHttpPort <= 0 then
                Error "basicHttpPort must be positive"
            elif config.udpPort <= 0 then
                Error "udpPort must be positive"
            elif String.IsNullOrWhiteSpace(config.clientId) then
                Error "clientId is required"
            //elif String.IsNullOrWhiteSpace(config.serverId) then
            //    Error "serverId is required"
            elif String.IsNullOrWhiteSpace(config.clientPrivateKey) then
                Error "clientPrivateKey is required"
            elif String.IsNullOrWhiteSpace(config.clientPublicKey) then
                Error "clientPublicKey is required"
            elif String.IsNullOrWhiteSpace(config.serverPublicKey) then
                Error "serverPublicKey is required"
            else
                Ok config
        with
        | ex -> Error $"Failed to parse config: '{ex.Message}'."


    let getPhysicalGatewayIp() = failwith "getPhysicalGatewayIp is not implemented yet."
    let getPhysicalInterfaceName() = failwith "getPhysicalInterfaceName is not implemented yet."


    /// Load config from file path.
    let tryLoadConfigFromFile (filePath: string) : Result<VpnClientConfig, string> =
        try
            let json = File.ReadAllText(filePath)
            tryLoadConfigFromJson json
        with
        | ex -> Error $"Failed to read config file: {ex.Message}"


    /// Convert VpnClientConfig to VpnClientServiceData.
    let toVpnClientServiceData (config: VpnClientConfig) : Result<VpnClientServiceData, string> =
        try
            let clientId = VpnClientId (Guid.Parse(config.clientId))
            let clientPrivateKey = PrivateKey config.clientPrivateKey
            let clientPublicKey = PublicKey config.clientPublicKey
            let serverPublicKey = PublicKey config.serverPublicKey
            let encryptionType = parseEncryptionType config.encryptionType
            let serviceAddress = IpAddress.Ip4 config.serverHost |> ServiceAddress
            let servicPort = config.udpPort |> ServicePort
            let serviceName = ServiceName AuthServiceName

            let httpServiceInfo = HttpServiceAccessInfo.create serviceAddress servicPort serviceName

            let clientAccessInfo : VpnClientAccessInfo =
                {
                    vpnClientId = clientId
                    vpnServerId = VpnServerId Guid.Empty // Not needed for Android client ???
                    serverAccessInfo = HttpServiceInfo httpServiceInfo
                    clientKeyPath = FolderName String.Empty // Not used on Android ???
                    serverPublicKeyPath = FolderName String.Empty // Not used on Android ???
                    localLanExclusions = []
                    vpnTransportProtocol = UDP_Push
                    physicalGatewayIp = getPhysicalGatewayIp()
                    physicalInterfaceName = getPhysicalInterfaceName()
                    useEncryption = config.useEncryption
                    encryptionType = encryptionType
                }

            let wcfHttpServiceInfo = HttpServiceAccessInfo.create serviceAddress servicPort (ServiceName AuthServiceName)

            let clientAccessInfoWithCorrectPort = { clientAccessInfo with serverAccessInfo = HttpServiceInfo wcfHttpServiceInfo }

            let serviceData : VpnClientServiceData =
                {
                    clientAccessInfo = clientAccessInfoWithCorrectPort
                    clientPrivateKey = clientPrivateKey
                    clientPublicKey = clientPublicKey
                    serverPublicKey = serverPublicKey
                }

            Ok serviceData
        with
        | ex -> Error $"Failed to convert config: {ex.Message}"


    /// Load and convert config from file.
    let tryLoadServiceDataFromFile (filePath: string) : Result<VpnClientServiceData, string> =
        tryLoadConfigFromFile filePath
        |> Result.bind toVpnClientServiceData


    /// Load and convert config from JSON string.
    let tryLoadServiceDataFromJson (json: string) : Result<VpnClientServiceData, string> =
        tryLoadConfigFromJson json
        |> Result.bind toVpnClientServiceData
