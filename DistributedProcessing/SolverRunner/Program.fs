namespace Softellect.DistributedProcessing.SolverRunner

open Argu
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.SolverRunner.CommandLine
open Softellect.DistributedProcessing.SolverRunner.Implementation
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.VersionInfo
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.ExitErrorCodes
open Softellect.Sys.AppSettings
open Softellect.Sys.Core

module Program =

    let solverRunnerMain<'D, 'P, 'X, 'C> solverId getUserProxy argv =
        setLogLevel()
        Logger.logInfo $"solverRunnerMain<{typeof<'D>.Name}, {typeof<'P>.Name}, {typeof<'X>.Name}, {typeof<'X>.Name}> - messagingDataVersion = '{messagingDataVersion}', argv: %A{argv}."
        checkMonitorData()

        let parser = ArgumentParser.Create<SolverRunnerArguments>(programName = solverProgramName.value)
        let results = parser.Parse argv

        match results.TryGetResult RunQueue |> Option.bind (fun e -> e |> RunQueueId |> Some) with
        | Some q ->
            let forceRun = results.TryGetResult ForceRun |> Option.defaultValue false
            let ctx = SolverRunnerSystemContext<'D, 'P, 'X, 'C>.create()
            let data = SolverRunnerData.create solverId q forceRun
            runSolverProcess<'D, 'P, 'X, 'C> ctx getUserProxy data
        | None ->
            Logger.logError $"runSolver - invalid command line arguments: '%A{argv}'."
            InvalidCommandLineArgs
