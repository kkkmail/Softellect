namespace Softellect.Vpn.ClientAdm

open Argu

module CommandLine =

    [<CliPrefix(CliPrefix.Dash)>]
    type GenerateKeysArgs =
        | [<Unique>] [<AltCommandLine("-f")>] Force of bool

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | Force _ -> "pass true to force re-generation of keys when they already exist."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        ExportPublicKeyArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-ofn")>] OutputFolderName of string
        | [<Unique>] [<AltCommandLine("-o")>] Overwrite of bool

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | OutputFolderName _ -> "full path to export client public key."
                | Overwrite _ -> "pass true to overwrite output file when it already exists."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        ImportServerKeyArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-ifn")>] InputFileName of string

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | InputFileName _ -> "full path + file name to import server public key from."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        StatusArgs =
        | [<Unique>] Verbose of bool

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | Verbose _ -> "show detailed status information."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        SetServerArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-a")>] Address of string
        | [<Unique>] [<AltCommandLine("-p")>] Port of int

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | Address _ -> "VPN server address (IP or hostname)."
                | Port _ -> "VPN server port (default: 5080)."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        DetectPhysicalNetworkArgs =
        | [<Hidden>] Placeholder

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | Placeholder -> ""


    and
        VpnClientAdmArgs =
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] GenerateKeys of ParseResults<GenerateKeysArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ExportPublicKey of ParseResults<ExportPublicKeyArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ImportServerKey of ParseResults<ImportServerKeyArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] Status of ParseResults<StatusArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] SetServer of ParseResults<SetServerArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] DetectPhysicalNetwork of ParseResults<DetectPhysicalNetworkArgs>

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | GenerateKeys _ -> "generates client encryption keys."
                | ExportPublicKey _ -> "exports client public key for registration with server."
                | ImportServerKey _ -> "imports server's public key."
                | Status _ -> "shows current VPN client configuration and status."
                | SetServer _ -> "configures VPN server address and port."
                | DetectPhysicalNetwork _ -> "detects physical network route and writes to appsettings.json."
