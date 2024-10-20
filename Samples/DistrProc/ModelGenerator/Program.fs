namespace Softellect.Samples.DistrProc.ModelGenerator

open Argu
open Softellect.DistributedProcessing.ModelGenerator.Program
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.Proxy.ModelGenerator
open Softellect.Samples.DistrProc.ModelGenerator.CommandLine

module Program =

    [<EntryPoint>]
    let main argv =

        let parser = ArgumentParser.Create<ModelGeneratorArgs>(programName = ProgramName)
        let parsedResults = parser.Parse argv
        let seedValue = parsedResults.GetResult(SeedValue)
        let delay = parsedResults.TryGetResult(Delay)
        let i = { seedValue = seedValue; delay = delay }

        let proxy =
            {
                getInitialData = fun _ -> i
                generateModel = TestSolverData.create i.delay
                getSolverInputParams = fun _ -> inputParams
                getSolverOutputParams = fun _ -> outputParams
            }

        let result = generateModel<TestInitialData, TestSolverData> proxy solverId argv
        printfn $"result: '%A{result}'."

        CompletedSuccessfully
