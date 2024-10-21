namespace Softellect.Samples.DistrProc.SolverRunner

open System
open System.IO
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.SolverRunner.Program
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.SolverRunner.OdeSolver
open Softellect.Sys.Primitives
open Wolfram.NETLink
open Plotly.NET
open Giraffe.ViewEngine
open Softellect.Sys.Wolfram

module Program =

    type ChartDescription =
        {
            Heading : string
            Text : string
        }

    type WolframRequest =
        {
            content : string // A content of .m file to be put into inputFolder\inputFileName
            inputFolder : string
            inputFileName : string
            outputFolder : string
            outputFileName : string
            extension : string
        }


    // Function to ensure a directory exists and create it if it doesn't
    let ensureDirectoryExists dir =
        if not (Directory.Exists dir) then
            Directory.CreateDirectory dir |> ignore


    let runMathematicaScript (request: WolframRequest) =
        try
            // Ensure input and output folders exist
            ensureDirectoryExists request.inputFolder
            ensureDirectoryExists request.outputFolder

            // Write the content to the input file
            let inputFilePath = Path.Combine(request.inputFolder, request.inputFileName)
            File.WriteAllText(inputFilePath, request.content)

           // Start the Wolfram Kernel with explicit link name
            let linkArgs = "-linkname 'C:\\Program Files\\Wolfram Research\\Mathematica\\13.0\\mathkernel.exe -mathlink'"
            let link = MathLinkFactory.CreateKernelLink(linkArgs)

            try
                // Discard the initial kernel output
                link.WaitAndDiscardAnswer()

                // Load the .m file as a script and run it
                let scriptCommand = $"<< \"%s{inputFilePath}\""  // Use "<< file.m" to load the script
                link.Evaluate(scriptCommand)

                // Wait for the result of the evaluation
                link.WaitForAnswer() |> ignore

                // Check for the output file in the output folder
                let outputFilePath = Path.Combine(request.outputFolder, request.outputFileName) + "." + request.extension
                if File.Exists(outputFilePath) then
                    // If the output file is found, read it as a byte array and return it as Ok
                    let fileBytes = File.ReadAllBytes(outputFilePath)
                    link.Close() // Close the link when done
                    Ok fileBytes
                else
                    // If the output file is not found, return an error
                    link.Close()
                    Error $"Output file '{outputFilePath}' not found."

            with
            | ex ->
                link.Close()
                Error $"An error occurred during Wolfram evaluation: {ex.Message}"

        with
        | ex -> Error $"An error occurred: {ex.Message}"


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


    let inputFolder = "C:\\\\Temp\\\\WolframInput\\\\"
    let outputFolder = "C:\\\\Temp\\\\WolframOutput\\\\"
    let getInputFileName (q : RunQueueId) = $"{q.value}.m"
    let getOutputFileName (q : RunQueueId) = $"{q.value}"


    let getWolframData (q : RunQueueId) (d : TestSolverData) (c : list<ChartSliceData<TestChartData>>) =
        let data =
            [
                $"t = {{0, 1, 2, 3, 4, 5}};"
                $"x1 = {{0, 1, 2, 3, 4, 5}};"
                $"x2 = {{5, 4, 3, 2, 1, 0}};"
                $"tx1 = Table[{{t[[i]], x1[[i]]}}, {{i, 1, Length[t]}}];"
                $"tx2 = Table[{{t[[i]], x2[[i]]}}, {{i, 1, Length[t]}}];"
                $"legends = {{\"Prey\", \"Predator\"}};"
                $"outputFile = \"{outputFolder}{(getOutputFileName q)}.png\";"
                $"Export[outputFile, ListLinePlot[{{tx1, tx2}}, Frame -> True, GridLines -> Automatic, PlotLegends -> legends], \"PNG\"];"
            ]
            |> joinStrings Nl

        data


    let getWolframChart (q : RunQueueId) (d : TestSolverData) (c : list<ChartSliceData<TestChartData>>) =
        let data = getWolframData q d c
        let request =
            {
                content = data
                inputFolder = inputFolder
                inputFileName = getInputFileName q
                outputFolder = outputFolder
                outputFileName = getOutputFileName q
                extension = "png"
            }

        match runMathematicaScript request with
        | Ok v ->
            {
                binaryContent = v
                fileName = FileName (request.outputFileName + "." + request.extension)
            }
            |> BinaryChart
            |> Some
        | Error e ->
            printfn $"getWolframChart - Error: %A{e}."
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
                        getChartData = fun _ t (x : double[]) -> { t = double t.value; x = x }
                        generateCharts = fun q d _ c -> getCharts q d c
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
