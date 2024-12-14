namespace Softellect.DistributedProcessing.PartitionerAdm

open Argu
open System
open Softellect.Sys.Primitives
open Softellect.DistributedProcessing.Primitives.Common

module CommandLine =

    /// See: https://fsprojects.github.io/Argu/tutorial.html
    [<CliPrefix(CliPrefix.Dash)>]
    type AddSolverArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-i")>] SolverId of Guid
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-n")>] Name of string
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-s")>] Folder of string
        |               [<Unique>] [<AltCommandLine("-d")>] Description of string

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | SolverId _ -> "solver id."
                | Name _ -> "solver name."
                | Folder _ -> "solver folder."
                | Description _ -> "solver description."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        SendSolverArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-i")>] SolverId of Guid
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-w")>] WorkerNodeId of Guid

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | SolverId _ -> "solver id."
                | WorkerNodeId _ -> "worker node id."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        SendAllSolversArgs =
        | [<Unique>] [<AltCommandLine("-f")>] Force of bool

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | Force _ -> "pass true to force re-sending all solvers to all worker nodes."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        ModifyRunQueueArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-q")>] RunQueueIdToModify of Guid
        |               [<Unique>] [<AltCommandLine("-c")>] CancelOrAbort of bool
        |               [<Unique>] [<AltCommandLine("-r")>] ReportResults of bool
        |               [<Unique>] [<AltCommandLine("-e")>] ResetIfFailed

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | RunQueueIdToModify _ -> "RunQueueId to modify."
                | CancelOrAbort _ -> "if false then requests to cancel with results, if true then requests to abort calculations."
                | ReportResults _ -> "if false then requests results without results, if true the requests results."
                | ResetIfFailed -> "if present then reset a failed run queue. That's run queues with status = Failed."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        GenerateKeysArgs =
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
        PartitionerAdmArgs =
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] AddSolver of ParseResults<AddSolverArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] SendSolver of ParseResults<SendSolverArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] SendAllSolvers of ParseResults<SendAllSolversArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ModifyRunQueue of ParseResults<ModifyRunQueueArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] GenerateKeys of ParseResults<GenerateKeysArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ExportPublicKey of ParseResults<ExportPublicKeyArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ImportPublicKey of ParseResults<ImportPublicKeyArgs>

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | AddSolver _ -> "add a solver."
                | SendSolver _ -> "send a solver to a worker node."
                | SendAllSolvers _ -> "send all solver to all worker nodes."
                | ModifyRunQueue _ -> "tries to modify a run queue."
                | GenerateKeys _ -> "generates encryption keys."
                | ExportPublicKey _ -> "exports partitioner public key."
                | ImportPublicKey _ -> "import worker node public key."
