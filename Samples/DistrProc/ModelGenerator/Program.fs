namespace Softellect.Samples.DistrProc.ModelGenerator

open Softellect.DistributedProcessing.ModelGenerator.Program
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.Proxy.ModelGenerator

module Program =

    [<EntryPoint>]
    let main argv =

        let proxy =
            {
                getInitialData = failwith ""
                generateModel = failwith ""
                getSolverInputParams = fun _ ->
                    {
                        startTime = EvolutionTime 0m
                        endTime = EvolutionTime 1_000m
                    }
                getSolverOutputParams = fun _ -> SolverOutputParams.defaultValue
            }

        let result = generateModel<TestInitialData, TestSolverData> proxy solverId argv
        printfn $"result: '{result}'."

        CompletedSuccessfully
