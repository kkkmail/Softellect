namespace Softellect.DistributedProcessing.PartitionerAdm

open Argu
open System

module CommandLine =

    /// See: https://fsprojects.github.io/Argu/tutorial.html
    [<CliPrefix(CliPrefix.Dash)>]
    type AddSolverArgs =
        | [<Mandatory>] [<AltCommandLine("-i")>] SolverId of Guid
        | [<Mandatory>] [<AltCommandLine("-n")>] Name of string
        | [<Mandatory>] [<AltCommandLine("-s")>] Folder of string
        | [<Unique>]    [<AltCommandLine("-d")>] Description of string

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | SolverId _ -> "solver id."
                | Name _ -> "solver name."
                | Folder _ -> "solver folder."
                | Description _ -> "solver description."


    and SendSolverArgs =
        | [<Mandatory>] [<AltCommandLine("-i")>] SolverId of Guid
        | [<Mandatory>] [<AltCommandLine("-w")>] WorkerNodeId of Guid

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | SolverId _ -> "solver id."
                | WorkerNodeId _ -> "worker node id."


    and PartitionerAdmArgs =
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] AddSolver of ParseResults<AddSolverArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] SendSolver of ParseResults<SendSolverArgs>

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | AddSolver _ -> "add a solver."
                | SendSolver _ -> "send a solver to a worker node."
