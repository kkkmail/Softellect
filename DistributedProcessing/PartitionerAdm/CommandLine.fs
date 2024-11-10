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


    and SendSolverArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-i")>] SolverId of Guid
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-w")>] WorkerNodeId of Guid

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | SolverId _ -> "solver id."
                | WorkerNodeId _ -> "worker node id."


    and
        [<CliPrefix(CliPrefix.Dash)>]
        ModifyRunQueueArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-q")>] RunQueueIdToModify of Guid
        |               [<Unique>] [<AltCommandLine("-c")>] CancelOrAbort of bool
        |               [<Unique>] [<AltCommandLine("-r")>] ReportResults of bool
        |               [<Unique>] [<AltCommandLine("-e")>] ResetIfFailed

        with
        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | RunQueueIdToModify _ -> "RunQueueId to modify."
                | CancelOrAbort _ -> "if false then requests to cancel with results, if true then requests to abort calculations."
                | ReportResults _ -> "if false then requests results without results, if true the requests results."
                | ResetIfFailed -> "if present then reset a failed run queue. That's run queues with status = Failed."


    and PartitionerAdmArgs =
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] AddSolver of ParseResults<AddSolverArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] SendSolver of ParseResults<SendSolverArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] ModifyRunQueue of ParseResults<ModifyRunQueueArgs>

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | AddSolver _ -> "add a solver."
                | SendSolver _ -> "send a solver to a worker node."
                | ModifyRunQueue _ -> "tries to modify a run queue."


    let tryGetRunQueueIdToModify p = p |> List.tryPick (fun e -> match e with | RunQueueIdToModify e -> e |> RunQueueId |> Some | _ -> None)

    let getCancellationTypeOpt p =
        p |> List.tryPick (fun e -> match e with | CancelOrAbort e -> (match e with | false -> (CancelWithResults None) | true -> (AbortCalculation None)) |> Some | _ -> None)


    let getResultNotificationTypeOpt p =
        p |> List.tryPick (fun e -> match e with | ReportResults e -> (match e with | false -> RegularResultGeneration | true -> ForceResultGeneration) |> Some | _ -> None)

    let getResetIfFailed p = p |> List.tryPick (fun e -> match e with | ResetIfFailed -> Some true | _ -> None) |> Option.defaultValue false
