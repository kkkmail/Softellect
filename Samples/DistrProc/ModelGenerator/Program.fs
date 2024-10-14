namespace Softellect.Samples.DistrProc.ModelGenerator

open Softellect.DistributedProcessing.ModelGenerator.Program
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.Proxy.ModelGenerator
open System

module Program =
    /// Expects zero ro one command line parameters.
    /// If one parameter is supplied, then it is expected to be an integer random seed.
    [<EntryPoint>]
    let main argv =

        let seedValue =
            match argv with
            | [| seed |] -> int seed
            | _ -> Random().Next()

        let proxy =
            {
                getInitialData = fun _ -> { seedValue = seedValue }
                generateModel = TestSolverData.create
                getSolverInputParams = fun _ -> inputParams
                getSolverOutputParams = fun _ -> outputParams
            }

        let result = generateModel<TestInitialData, TestSolverData> proxy solverId argv
        printfn $"result: '%A{result}'."

        CompletedSuccessfully
