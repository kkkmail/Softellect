namespace Softellect.DistributedProcessing.WorkerNodeAdm

open Argu
open System
open Softellect.Sys.Primitives
open Softellect.DistributedProcessing.Primitives.Common

module CommandLine =

    /// See: https://fsprojects.github.io/Argu/tutorial.html
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
                | OutputFolderName _ -> "full path to export partitioner public key."
                | Overwrite _ -> "pass true to overwrite output file when it already exists."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        ImportPublicKeyArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-ifn")>] InputFileName of string

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | InputFileName _ -> "full path + file name to import worker node public key from."


    and
        WorkerNodeAdmArgs =
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] GenerateKeys of ParseResults<GenerateKeysArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ExportPublicKey of ParseResults<ExportPublicKeyArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ImportPublicKey of ParseResults<ImportPublicKeyArgs>

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | GenerateKeys _ -> "generates encryption keys."
                | ExportPublicKey _ -> "exports worker node public key."
                | ImportPublicKey _ -> "import partitioner public key."
