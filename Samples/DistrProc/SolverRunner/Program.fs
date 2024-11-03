namespace Softellect.Samples.DistrProc.SolverRunner

open System
open Softellect.Sys.AppSettings
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.SolverRunner.Program
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.SolverRunner.OdeSolver
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Plotly.NET
open Giraffe.ViewEngine
open Softellect.Analytics.Wolfram
open Softellect.Analytics.AppSettings

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
            htmlContent = toEmbeddedHtmlWithDescription d ch
            fileName = toHtmlFileName fileName
        }
        |> HtmlChart


    let tryGetInputFileName inputFolder (q : RunQueueId) = (FileName $"{q.value}.m").tryGetFullFileName (Some inputFolder)
    let tryGetOutputFileName outputFolder (q : RunQueueId) = (FileName $"{q.value}.png").tryGetFullFileName (Some outputFolder)


    let getWolframData (q : RunQueueId) (d : TestSolverData) (c : list<ChartSliceData<TestChartData>>) =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let outputFolder = provider.getWolframOutputFolder()
            let c1 = c |>  List.rev

            match c1 |> List.tryHead, tryGetOutputFileName outputFolder q with
            | Some h, Ok o ->
                let t = c1 |> List.map(fun e -> double e.t)
                let legends = d.chartLabels

                let x =
                    h.chartData.x
                    |> Array.mapi (fun i  _ -> c1 |> List.map (fun e -> e.chartData.x[i]))

                let xValues = x |> Array.mapi(fun i e -> $"x{i} = {(toWolframNotation e)};") |> List.ofArray
                let txValues = x |> Array.mapi(fun i _ -> $"tx{i} = Table[{{t[[i]], x{i}[[i]]}}, {{i, 1, Length[t]}}];") |> List.ofArray
                let txVar = x |> Array.mapi(fun i _ -> $"tx{i}") |> joinStrings ", "

                let data =
                    [
                        $"t = {(toWolframNotation t)};"
                    ]
                    @
                    xValues
                    @
                    txValues
                    @
                    [
                        $"legends = {(toWolframNotation legends)};"
                        $"outputFile = \"{o.toWolframNotation()}\";"
                        $"Export[outputFile, ListLinePlot[{{{txVar}}}, Frame -> True, PlotRange -> All, GridLines -> Automatic, PlotLegends -> legends], \"PNG\"];"
                    ]
                    |> joinStrings Nl

                data |> WolframCode |> Some
            | None, _ -> None
            | _, Error e ->
                printfn $"getWolframData - ERROR: %A{e}."
                None
        | Error e ->
            printfn $"getWolframData - ERROR: %A{e}."
            None


    let getWolframChart (q : RunQueueId) (d : TestSolverData) (c : list<ChartSliceData<TestChartData>>) =
        try
            match AppSettingsProvider.tryCreate() with
            | Ok provider ->
                let inputFolder = provider.getWolframInputFolder()
                let outputFolder = provider.getWolframOutputFolder()
                provider.save()

                match getWolframData q d c, tryGetInputFileName inputFolder q, tryGetOutputFileName outputFolder q with
                | Some data, Ok i, Ok o ->
                    let request =
                        {
                            wolframCode = data
                            inputFileName = i
                            outputFileName = o
                        }

                    match tryRunMathematicaScript request with
                    | Ok v ->
                        {
                            binaryContent = v
                            fileName = request.outputFileName
                        }
                        |> BinaryChart
                        |> Some
                    | Error e ->
                        printfn $"getWolframChart - Error: %A{e}."
                        None
                | _ ->
                    printfn $"getWolframChart - Cannot get data for: %A{q}."
                    None
            | Error e ->
                printfn $"getWolframChart - ERROR: %A{e}."
                None
        with
        | e ->
            printfn $"getWolframChart - Exception: %A{e}."
            None


    let getCharts (q : RunQueueId) (d : TestSolverData) (c : list<ChartSliceData<TestChartData>>) =
        printfn $"getChart - q: '%A{q}', c.Length: '%A{c.Length}'."

        let charts =
            match c |> List.tryHead with
            | Some h ->
                h.chartData.x
                |> Array.mapi (fun i  _ -> Chart.Line(c |> List.map (fun c -> c.t, c.chartData.x[i]), Name = d.chartLabels[i]))
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
                let chartGenerator =
                    {
                        getChartData = fun _ _ (x : double[]) -> { x = x }
                        generateCharts = fun q d _ c -> getCharts q d c
                        generateDetailedCharts = fun _ _ _ _ -> None
                    }

                let getUserProxy (solverData : TestSolverData) =
                    let solverRunner = createOdeSolver solverData.inputParams solverData.odeParams

                    let solverProxy =
                        {
                            getInitialData = _.initialValues
                            getProgressData = None
                            getInvariant = fun _ _ _ -> RelativeInvariant 1.0
                        }

                    {
                        solverRunner = solverRunner
                        solverProxy = solverProxy
                        chartGenerator = chartGenerator
                    }

                // Call solverRunnerMain<'D, 'P, 'X, 'C>
                printfn "Calling solverRunnerMain..."
                solverRunnerMain<TestSolverData, TestProgressData, double[], TestChartData> solverId getUserProxy argv
            with
            | e ->
                Console.WriteLine($"Exception: %A{e}.")
                CriticalError

        // Console.ReadLine() |> ignore
        retVal
