namespace Softellect.Samples.DistrProc.ModelGenerator

open Argu
open Softellect.DistributedProcessing.ModelGenerator.Program
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.Proxy.ModelGenerator
open Softellect.Samples.DistrProc.ModelGenerator.CommandLine
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Logging

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
                startTime = EvolutionTime.defaultValue
                endTime = evolutionTime
            }

        let userProxy =
            {
                getInitialData = fun () -> i
                generateModelData = TestSolverData.create
                getSolverInputParams = fun _ -> inputParams
                getSolverOutputParams = fun _ -> outputParams
            }

        let systemProxy = ModelGeneratorSystemProxy.create()

        let result = generateModel<TestInitialData, TestSolverData> systemProxy solverId userProxy
        Logger.logInfo $"result: '%A{result}'."

        CompletedSuccessfully
