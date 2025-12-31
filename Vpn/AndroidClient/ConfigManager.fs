namespace Softellect.Vpn.AndroidClient

open System
open System.IO
open System.Collections.Generic
open System.Threading
open Newtonsoft.Json
open Softellect.Sys.Primitives
open Softellect.Sys.Logging
open Softellect.Wcf.Common
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Android.App
open Android.Content
open Android.Net
open Android.Net.Wifi
open Android.Provider
open Java.Net
open Android.Media
open System.Text


/// Thread-safe ring buffer for log messages.
/// Used to capture logs for UI display.
module LogBuffer =

    let private maxLines = 4096
    let private buffer = Array.zeroCreate<string> maxLines
    let mutable private head = 0
    let mutable private count = 0
    let private gate = obj()

    /// Event raised when a new log line is added.
    let mutable onLogAdded: (unit -> unit) option = None

    /// Add a log line to the buffer.
    let addLine (line: string) =
        lock gate (fun () ->
            buffer.[head] <- line
            head <- (head + 1) % maxLines
            if count < maxLines then count <- count + 1
        )
        match onLogAdded with
        | Some handler -> handler()
        | None -> ()

    /// Get all lines in chronological order.
    let getLines() : string[] =
        lock gate (fun () ->
            if count = 0 then [||]
            elif count < maxLines then
                // Buffer not yet wrapped
                Array.sub buffer 0 count
            else
                // Buffer wrapped - oldest is at head, newest at head-1
                let result = Array.zeroCreate count
                let start = head
                for i in 0 .. count - 1 do
                    result.[i] <- buffer.[(start + i) % maxLines]
                result
        )

    /// Get all lines as a single string with newlines.
    let getText() : string =
        getLines() |> String.concat "\n"

    /// Clear the buffer.
    let clear() =
        lock gate (fun () ->
            head <- 0
            count <- 0
        )

    /// Get current line count.
    let lineCount() =
        lock gate (fun () -> count)


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

            /// Server ID (GUID string).
            serverId : string

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
                serverId = ""
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


    /// Network type for display purposes.
    type NetworkType =
        | WiFi
        | Cellular
        | Unknown

        member this.ToDisplayString() =
            match this with
            | WiFi -> "Wi-Fi"
            | Cellular -> "Cellular"
            | Unknown -> "Unknown"


    /// Requires Android permission: android.permission.ACCESS_NETWORK_STATE
    /// Determines the current network type (Wi-Fi, Cellular, or Unknown).
    let getNetworkType() : NetworkType =
        try
            let ctx = Application.Context
            let cm = ctx.GetSystemService(Context.ConnectivityService) :?> ConnectivityManager
            let net = cm.ActiveNetwork
            if isNull net then Unknown
            else
                let caps = cm.GetNetworkCapabilities(net)
                if isNull caps then Unknown
                elif caps.HasTransport(TransportType.Wifi) then WiFi
                elif caps.HasTransport(TransportType.Cellular) then Cellular
                else Unknown
        with
        | _ -> Unknown


    /// Requires Android permission: android.permission.ACCESS_NETWORK_STATE
    let getPhysicalInterfaceName() =
        let ctx = Application.Context
        let cm = ctx.GetSystemService(Context.ConnectivityService) :?> ConnectivityManager
        let net = cm.ActiveNetwork
        if isNull net then failwith "No active network."
        let lp = cm.GetLinkProperties(net)
        if isNull lp then failwith "No LinkProperties for active network."
        let ifName = lp.InterfaceName
        if String.IsNullOrWhiteSpace ifName then failwith "Active network has no interface name."
        ifName

    /// Requires Android permission: android.permission.ACCESS_NETWORK_STATE
    /// Best-effort: prefers default route gateway from LinkProperties, falls back to WiFi DHCP gateway.
    let getPhysicalGatewayIp() =
        let ctx = Application.Context
        let cm = ctx.GetSystemService(Context.ConnectivityService) :?> ConnectivityManager
        let net = cm.ActiveNetwork
        if isNull net then failwith "No active network."

        let lp = cm.GetLinkProperties(net)
        let gwFromRoutes =
            if isNull lp then None
            else
                lp.Routes
                |> Seq.cast<RouteInfo>
                |> Seq.tryPick (fun r ->
                    // Default route has a gateway; take it
                    if r.IsDefaultRoute then
                        let gw = r.Gateway
                        if isNull gw then None
                        else Some gw.HostAddress
                    else None)

        match gwFromRoutes with
        | Some gw when not (String.IsNullOrWhiteSpace gw) -> gw
        | _ ->
            // Fallback for Wi-Fi: DHCP gateway (works when LinkProperties doesn't expose it)
            let wm = ctx.GetSystemService(Context.WifiService) :?> WifiManager
            if isNull wm || isNull wm.DhcpInfo then
                failwith "Gateway not found in LinkProperties, and WiFi DHCP info unavailable."
            let g = wm.DhcpInfo.Gateway
            if g = 0 then failwith "WiFi DHCP gateway is 0."
            // DhcpInfo.Gateway is little-endian int
            let b1 = byte (g &&& 0xFF)
            let b2 = byte ((g >>> 8) &&& 0xFF)
            let b3 = byte ((g >>> 16) &&& 0xFF)
            let b4 = byte ((g >>> 24) &&& 0xFF)
            $"{b1}.{b2}.{b3}.{b4}"
        |> Ip4


    /// Spec 056: Get Android Secure ID for device binding.
    /// Returns Error if ANDROID_ID cannot be read.
    let getAndroidSecureId (context: Context) : Result<string, string> =
        try
            let contentResolver = context.ContentResolver
            let androidId = Settings.Secure.GetString(contentResolver, Settings.Secure.AndroidId)
            if String.IsNullOrEmpty(androidId) then
                Error "ANDROID_ID is null or empty"
            else
                Ok androidId
        with
        | ex -> Error $"Failed to read ANDROID_ID: {ex.Message}"


    /// Spec 056: Compute client hash from Android Secure ID.
    let getAndroidClientHash (context: Context) : Result<VpnClientHash, string> =
        match getAndroidSecureId context with
        | Ok androidId -> Ok (VpnClientHash.compute androidId)
        | Error e -> Error e


    let readConfigJson (context: Context) : string =
        use stream = context.Assets.Open("vpn_config.json")
        use reader = new StreamReader(stream, Encoding.UTF8)
        reader.ReadToEnd()


    /// Load config from file path.
    let tryLoadConfigFromFile (context: Context) : Result<VpnClientConfig, string> =
        try
            let json = readConfigJson context
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
            let servicePort = config.udpPort |> ServicePort
            let serviceName = ServiceName "VpnService"

            let httpServiceInfo = HttpServiceAccessInfo.create serviceAddress servicePort serviceName

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

            let wcfHttpServiceInfo = HttpServiceAccessInfo.create serviceAddress servicePort serviceName

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


    ///// Load and convert config from file.
    //let tryLoadServiceDataFromFile (filePath: string) : Result<VpnClientServiceData, string> =
    //    tryLoadConfigFromFile filePath
    //    |> Result.bind toVpnClientServiceData


    ///// Load and convert config from JSON string.
    //let tryLoadServiceDataFromJson (json: string) : Result<VpnClientServiceData, string> =
    //    tryLoadConfigFromJson json
    //    |> Result.bind toVpnClientServiceData
