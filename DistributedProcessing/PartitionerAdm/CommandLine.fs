namespace Softellect.DistributedProcessing.PartitionerAdm

open Argu
open System

module CommandLine =

    /// See: https://fsprojects.github.io/Argu/tutorial.html
    [<CliPrefix(CliPrefix.Dash)>]
    type SolverArgs =
        | [<Mandatory>] [<AltCommandLine("-i")>] Id of Guid
        | [<Mandatory>] [<AltCommandLine("-n")>] Name of string
        | [<Mandatory>] [<AltCommandLine("-s")>] Folder of string
        | [<Unique>]    [<AltCommandLine("-d")>] Description of string

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | Id _ -> "solver id."
                | Name _ -> "solver name."
                | Folder _ -> "solver folder."
                | Description _ -> "solver description."


    and TestArgs =
        | [<Mandatory>] [<AltCommandLine("-t")>] TestId of Guid

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | TestId _ -> "id."


    and PartitionerAdmArgs =
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] AddSolver of ParseResults<SolverArgs>
        | [<Unique>] [<CliPrefix(CliPrefix.None)>] Test of ParseResults<TestArgs>

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | AddSolver _ -> "add a solver."
                | Test _ -> "test."

