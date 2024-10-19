namespace Softellect.Samples.DistrProc.SolverRunner

open System
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.SolverRunner.Program
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.SolverRunner.OdeSolver
open Softellect.Sys.Primitives

open Plotly.NET
open Giraffe.ViewEngine

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


    let getChart (q : RunQueueId) (c : list<ChartSliceData<TestChartData>>) : list<Softellect.Sys.Primitives.Chart> option =
        printfn $"getChart - q: '%A{q}', c: '%A{c}'."

        let charts =
            match c |> List.tryHead with
            | Some h ->
                h.chartData.x
                |> Array.mapi (fun i  _ -> Chart.Line(c |> List.map (fun c -> c.t, c.chartData.x[i])))
            | None -> [||]
            // [
            //     Chart.Line(c |> List.map (fun c -> c.t, c.chartData.x))
            //     Chart.Line(c |> List.map (fun c -> c.t, c.chartData.y))
            //     Chart.Line(c |> List.map (fun c -> c.t, c.chartData.z))
            // ]

        let chart = Chart.combine charts
        [ getHtmlChart (FileName $"{q.value}") (toDescription "Heading" "Text") chart ] |> Some


    [<EntryPoint>]
    let main argv =
        let retVal =
            try
                let chartGenerator =
                    {
                        getChartData = fun _ t (x : double[]) -> { t = double t.value; x = x }
                        generateCharts = fun q _ _ c -> getChart q c
                        generateDetailedCharts = fun _ _ _ _ -> []
                    }

                let getUserProxy (solverData : TestSolverData) =
                    let solverRunner = createOdeSolver inputParams solverData.odeParams

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

        Console.ReadLine() |> ignore
        retVal
