namespace Softellect.Vpn.Server

open System.IO
open System.Diagnostics
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
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

#if LINUX
open Softellect.Vpn.LinuxServer.TunAdapter
#endif

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
        match data.serverAccessInfo.vpnTransportProtocol with
        | UDP_Push ->
            let authService = AuthService(data)
            let service = VpnPushService(data, authService.clientRegistry)

            let configureServices (serviceCollection : IServiceCollection) =
                serviceCollection.AddSingleton<IHostedService>(service :> IHostedService) |> ignore
                let combinedUdpHostedService = getCombinedUdpHostedService data service authService.clientRegistry
                serviceCollection.AddSingleton<IHostedService>(combinedUdpHostedService :> IHostedService) |> ignore

            let getAuthService() = authService
            getAuthWcfProgram data getAuthService argv (Some configureServices)

#if LINUX
    /// Adds iptables rule to drop outgoing RST packets for VPN NAT port range.
    /// This prevents the Linux kernel from interfering with raw socket TCP traffic.
    /// Returns true if successful, false otherwise.
    let tryAddVpnIptablesRule () =
        runCommand "iptables" "-A OUTPUT -p tcp --sport 40000:65535 --tcp-flags RST RST -j DROP" "add iptables rule"


    let getPrimaryInterfaceName () =
        let defaultInterfaceName = "eth0"

        let parseDev (output : string) =
            let parts = output.Split([|' '; '\n'; '\t'|], System.StringSplitOptions.RemoveEmptyEntries)

            parts
            |> Array.tryFindIndex ((=) "dev")
            |> Option.bind (fun i -> if i + 1 < parts.Length then Some parts[i + 1] else None)

        try
            let startInfo =
                ProcessStartInfo(
                    FileName = "/sbin/ip",
                    Arguments = "route get 1.1.1.1",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                )

            use proc = Process.Start(startInfo)
            proc.WaitForExit(10_000) |> ignore

            if proc.ExitCode <> 0 then
                let err = proc.StandardError.ReadToEnd()
                Logger.logWarn $"ip route failed, falling back to eth0. Error: '{err}'."
                defaultInterfaceName
            else
                let output = proc.StandardOutput.ReadToEnd()
                match parseDev output with
                | Some iface -> iface
                | None ->
                    Logger.logWarn $"could not parse interface from output '{output}', falling back to eth0."
                    defaultInterfaceName
        with ex ->
            Logger.logWarn $"exception '{ex.Message}', falling back to eth0."
            defaultInterfaceName


    let disableNicOffloads () =
        let iface = getPrimaryInterfaceName()
        let runCommand = runCommand "ethtool"

        let step (args : string) (op : string) =
            match runCommand args op with
            | Ok () -> Ok ()
            | Error e when e.Contains("Could not change any device features") ->
                Logger.logWarn $"Non-fatal: '{op}' failed: '{e}'."
                Ok ()
            | Error e -> Error e

        // Run sequentially, stop on first error
        step $"-K {iface} gro off" "disable gro"
        |> Result.bind (fun () -> step $"-K {iface} rx-gro-hw off" "disable rx-gro-hw")
        |> Result.bind (fun () -> step $"-K {iface} lro off" "disable lro")
        |> Result.bind (fun () -> step $"-K {iface} tso off" "disable tso")
        |> Result.bind (fun () -> step $"-K {iface} gso off" "disable gso")
        |> Result.bind (fun () -> step $"-K {iface} tx off" "disable tx checksum offload")
        |> Result.bind (fun () -> step $"-K {iface} rx off" "disable rx checksum offload")
        |> Result.bind (fun () -> step $"-K {iface} rxvlan off" "disable rxvlan offload")
        |> Result.bind (fun () -> step $"-K {iface} txvlan off" "disable txvlan offload")
        |> Result.bind (fun () -> step $"-K {iface} tx-udp-segmentation off" "disable tx-udp-segmentation")
        |> Result.bind (fun () -> step $"-K {iface} tx-udp_tnl-segmentation off" "disable tx-udp_tnl-segmentation")
        |> Result.bind (fun () -> step $"-K {iface} tx-udp_tnl-csum-segmentation off" "disable tx-udp_tnl-csum-segmentation")
#else
    let tryAddVpnIptablesRule () = Ok ()
    let disableNicOffloads () = Ok ()
#endif

    let vpnServerMain argv =
        setLogLevel()
        let serverAccessInfo = loadVpnServerAccessInfo()

        Logger.logInfo $"vpnServerMain - serverAccessInfo = '{serverAccessInfo}'."

        match loadServerKeys serverAccessInfo.serverKeyPath with
        | Ok (privateKey, publicKey) ->
            match tryAddVpnIptablesRule () with
            | Ok () ->
                match disableNicOffloads () with
                | Ok () ->
                    let data =
                        {
                            serverAccessInfo = serverAccessInfo
                            serverPrivateKey = privateKey
                            serverPublicKey = publicKey
                        }

                    let program = getProgram data argv
                    program()
                | Error e ->
                    Logger.logCrit e
                    Softellect.Sys.ExitErrorCodes.CriticalError
            | Error e ->
                Logger.logCrit e
                Softellect.Sys.ExitErrorCodes.CriticalError
        | Error msg ->
            Logger.logCrit msg
            Softellect.Sys.ExitErrorCodes.CriticalError
