namespace Softellect.Samples.DistrProc.ModelGenerator

open Argu
open Softellect.DistributedProcessing.ModelGenerator.Program
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.Proxy.ModelGenerator
open Softellect.Samples.DistrProc.ModelGenerator.CommandLine
open Softellect.DistributedProcessing.Primitives.Common

module Program =

    [<EntryPoint>]
    let main argv =

        let parser = ArgumentParser.Create<ModelGeneratorArgs>(programName = ProgramName)
        let parsedResults = parser.Parse argv
        let seedValue = parsedResults.GetResult(SeedValue)
        let delay = parsedResults.TryGetResult(Delay)
        let evolutionTime = parsedResults.TryGetResult(RunTime) |> Option.defaultValue 1_000 |> decimal |> EvolutionTime
        let modelId = parsedResults.TryGetResult(ModelId) |> Option.defaultValue 1

        let i =
            {
                seedValue = seedValue
                delay = delay
                evolutionTime = evolutionTime
                modelId = modelId
            }

        let inputParams =
            {
                startTime = EvolutionTime 0m
                endTime = evolutionTime
            }

        let proxy =
            {
                getInitialData = fun _ -> i
                generateModel = TestSolverData.create
                getSolverInputParams = fun _ -> inputParams
                getSolverOutputParams = fun _ -> outputParams
            }

        let result = generateModel<TestInitialData, TestSolverData> proxy solverId argv
        printfn $"result: '%A{result}'."

        CompletedSuccessfully
