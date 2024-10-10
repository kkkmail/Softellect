namespace Softellect.DistributedProcessing.SolverRunner

open Argu
open Softellect.DistributedProcessing.SolverRunner.CommandLine
open Softellect.DistributedProcessing.SolverRunner.Implementation
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.VersionInfo

module Program =

    let solverRunnerMain<'D, 'P, 'X, 'C> solverId userProxy argv =
        printfn $"solverRunnerMain<{typeof<'D>.Name}, {typeof<'P>.Name}, {typeof<'X>.Name}, {typeof<'X>.Name}> - messagingDataVersion = '{messagingDataVersion}', argv: %A{argv}."

        let parser = ArgumentParser.Create<SolverRunnerArguments>(programName = SolverProgramName)
        let results = parser.Parse argv

        runSolverProcess<'D, 'P, 'X, 'C> solverId userProxy results
