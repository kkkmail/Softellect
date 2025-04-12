namespace Softellect.Samples.DistrProc.SolverRunner

open System
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.SolverRunner.Implementation
open Softellect.DistributedProcessing.SolverRunner.Program
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.SolverRunner.OdeSolver
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Plotly.NET
open Giraffe.ViewEngine
open Softellect.Analytics.Wolfram
open Softellect.Analytics.AppSettings
open Softellect.Analytics.Primitives

module Program =

    type ChartDescription =
        {
            Heading : string
            Text : string
        }


    let toDescription h t =
        {
            Heading = h
            Text = t
        }


    let toEmbeddedHtmlWithDescription (description : ChartDescription) (gChart : GenericChart) =
        let plotlyRef = PlotlyJSReference.Full

        let displayOpts =
            DisplayOptions.init(
                AdditionalHeadTags = [
                    script [_src description.Heading] []
                ],
                // Description = [
                //     h1 [] [str description.Heading]
                //     h2 [] [str description.Text]
                // ],
                PlotlyJSReference = plotlyRef
            )

        let result =
            gChart
            |> Chart.withDisplayOptions(displayOpts)
            |> GenericChart.toEmbeddedHTML

        result


    let toHtmlFileName (FileName fileName) =
        if fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) then fileName
        else fileName + ".html"
        |> FileName


    let getHtmlChart fileName d ch =
        {
            textContent = toEmbeddedHtmlWithDescription d ch
            fileName = toHtmlFileName fileName
        }
        |> TextResult


    let tryGetInputFileName inputFolder (q : RunQueueId) = (FileName $"{q.value}.m").tryGetFullFileName (Some inputFolder)
    let tryGetOutputFileName outputFolder (q : RunQueueId) = (FileName $"{q.value}.png").tryGetFullFileName (Some outputFolder)


    let getWolframChart (q : RunQueueId) (d : TestSolverData) (c : list<ResultSliceData<TestChartData>>) =
        let w = getSolverWolframParams solverId

        match tryGetInputFileName w.wolframInputFolder q, tryGetOutputFileName w.wolframOutputFolder q with
        | Ok i, Ok o ->
            let c1 = c |> List.rev
            let t = c1 |> List.map(fun e -> double e.t)
            let legends = d.chartLabels

            let d =
                c1.Head.resultData.x
                |> List.ofArray
                |> List.mapi (fun i  _ -> { dataLabel = legends[i] |> DataLabel; dataPoints = c1 |> List.mapi (fun j e -> { x = t[j]; y = e.resultData.x[i] }) })

            let p = { ListLineParams.defaultValue with imageSize = UserDefinedImageSize "1000" |> Some}

            getListLinePlot i o p d
        | _ ->
            Logger.logError $"getWolframChart - Cannot get data for: %A{q}."
            None


    let getCharts (q : RunQueueId) (d : TestSolverData) (c : list<ResultSliceData<TestChartData>>) =
        Logger.logTrace $"getChart - q: '%A{q}', c.Length: '%A{c.Length}'."

        let charts =
            match c |> List.tryHead with
            | Some h ->
                h.resultData.x
                |> Array.mapi (fun i  _ -> Chart.Line(c |> List.map (fun c -> c.t, c.resultData.x[i]), Name = d.chartLabels[i]))
            | None -> [||]

        let chart = Chart.combine charts

        [
            getHtmlChart (FileName $"{q.value}") (toDescription "Heading" "Text") chart |> Some
            getWolframChart q d c
        ]
        |> List.choose id
        |> Some


    [<EntryPoint>]
    let main argv =
        let retVal =
            try
                // To check that invariant is actually passed back.
                let rnd = Random()
                let getInvariant() = (1.0 + (rnd.NextDouble() - 0.5) * 0.0001) |> RelativeInvariant

                let chartGenerator =
                    {
                        getResultData = fun _ _ (x : double[]) -> { x = x }
                        generateResults = fun q d _ c -> getCharts q d c
                        generateDetailedResults = fun _ _ _ _ -> None
                    }

                let getUserProxy (solverData : TestSolverData) =
                    let solverRunner = createOdeSolver solverData.inputParams solverData.odeParams

                    let solverProxy =
                        {
                            getInitialData = _.initialValues
                            getProgressData = None
                            getInvariant = fun _ _ _ -> getInvariant()
                            getOptionalFolder = fun _ _ -> None
                        }

                    {
                        solverRunner = solverRunner
                        solverProxy = solverProxy
                        resultGenerator = chartGenerator
                    }

                // Call solverRunnerMain<'D, 'P, 'X, 'C>
                Logger.logTrace "Calling solverRunnerMain..."
                solverRunnerMain<TestSolverData, TestProgressData, double[], TestChartData> solverId getUserProxy argv
            with
            | e ->
                Logger.logCrit($"Exception: %A{e}.")
                CriticalError

        // Console.ReadLine() |> ignore
        retVal
