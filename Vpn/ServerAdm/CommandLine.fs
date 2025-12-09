namespace Softellect.Vpn.ServerAdm

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
                | OutputFolderName _ -> "full path to export server public key."
                | Overwrite _ -> "pass true to overwrite output file when it already exists."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        ImportClientKeyArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-ifn")>] InputFileName of string

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | InputFileName _ -> "full path + file name to import client public key from."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        RegisterClientArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-id")>] ClientId of string
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-n")>] ClientName of string
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-ip")>] AssignedIp of string

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | ClientId _ -> "client GUID identifier."
                | ClientName _ -> "friendly name for the client."
                | AssignedIp _ -> "VPN IP address to assign to this client (e.g., 10.66.77.2)."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        ListClientsArgs =
        | [<Unique>] Verbose of bool

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | Verbose _ -> "show detailed client information."


    and
        VpnServerAdmArgs =
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] GenerateKeys of ParseResults<GenerateKeysArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ExportPublicKey of ParseResults<ExportPublicKeyArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ImportClientKey of ParseResults<ImportClientKeyArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] RegisterClient of ParseResults<RegisterClientArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ListClients of ParseResults<ListClientsArgs>

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | GenerateKeys _ -> "generates server encryption keys."
                | ExportPublicKey _ -> "exports server public key for distribution to clients."
                | ImportClientKey _ -> "imports a client's public key."
                | RegisterClient _ -> "registers a new client with assigned IP."
                | ListClients _ -> "lists all registered clients."
