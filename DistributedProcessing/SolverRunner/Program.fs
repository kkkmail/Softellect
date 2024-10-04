namespace Softellect.DistributedProcessing.SolverRunner

open Argu
open Softellect.DistributedProcessing.SolverRunner.CommandLine
open Softellect.DistributedProcessing.SolverRunner.Implementation
open Softellect.DistributedProcessing.Primitives.Common

module Program =

    let main<'D, 'P, 'X, 'C> solverId userProxy argv =
        printfn $"main<{typeof<'D>.Name}, {typeof<'P>.Name}, {typeof<'X>.Name}, {typeof<'X>.Name}> - messagingDataVersion = '{messagingDataVersion}', argv: %A{argv}."

        let parser = ArgumentParser.Create<SolverRunnerArguments>(programName = SolverProgramName)
        let results = parser.Parse argv

        runSolverProcess<'D, 'P, 'X, 'C> solverId userProxy results
