namespace Softellect.Analytics

open System
open Microsoft.FSharp.Reflection
open System.Text
open Softellect.Analytics.Primitives
open Softellect.Sys.AppSettings
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open System.IO
open Wolfram.NETLink
open Softellect.Analytics.AppSettings

/// A collection of functions to convert F# objects to Wolfram Language notation.
module Wolfram =

    ///. An encapsulation of Wolfram code file content.
    type WolframCode =
        | WolframCode of string

        member this.value = let (WolframCode v) = this in v


    type PlotRange =
        | All
        | Full
        | Automatic

        static member defaultValue = PlotRange.All


    type GridLines =
        | Automatic
        // | None
        | UserDefined of string // You are on your own here.

        static member defaultValue = GridLines.Automatic


    type ImageSize =
        | Tiny
        | Small
        | Medium
        | Large
        | Full

        static member defaultValue = ImageSize.Large


    type LabelStyle =
        | LabelStyle of string

        member this.value = let (LabelStyle v) = this in v

        static member defaultValue = LabelStyle "{FontSize -> 16, Bold, Black}"


    let private baseIndent = "  "
    let isDiscriminatedUnion obj = FSharpType.IsUnion(obj.GetType())
    let isRecord obj = FSharpType.IsRecord(obj.GetType())
    let isArray obj = obj <> null && obj.GetType().IsArray
    let isTuple obj = obj <> null && FSharpType.IsTuple(obj.GetType())


    /// Helper function to check if object is a sequence but not a string
    let isSeq obj =
        if obj = null then false
        elif obj.GetType().GetInterface(typeof<System.Collections.IEnumerable>.FullName) <> null &&
             obj.GetType() <> typeof<string> then true
        else false


    let isList obj =
        obj <> null && obj.GetType().IsGenericType &&
        obj.GetType().GetGenericTypeDefinition() = typedefof<List<_>>


    let isSimpleType obj =
        obj <> null &&
        not (isDiscriminatedUnion obj || isRecord obj) &&
        (obj.GetType().IsPrimitive || obj.GetType() = typeof<string> || obj.GetType() = typeof<decimal> || obj.GetType() = typeof<DateTime> || isTuple obj)


    let toWolframNotation v =
        // Helper function to get depth of sequences and records
        let rec seqDepth currentDepth (x : obj) =
            match x with
            | _ when isSeq x ->
                let seq = x :?> System.Collections.IEnumerable |> Seq.cast<_>
                if Seq.isEmpty seq then currentDepth + 1
                else Seq.map (seqDepth (currentDepth + 1)) seq |> Seq.max
            | _ when FSharpType.IsRecord(x.GetType()) ->
                let propertyValues =
                    FSharpType.GetRecordFields(x.GetType())
                    |> Array.map (fun fi -> fi.GetValue(x, null))
                Array.map (seqDepth currentDepth) propertyValues |> Array.max
            | _ -> currentDepth

        let rec inner (nestingLevel : int) (maxDepth: int) (x : obj) =
            let seqSeparator =
                if nestingLevel = (maxDepth - 1) then ", "
                else $",{Nl}"

            match x with
            | :? float as num -> $"{num:E}".Replace("E", "*^")
            | :? string as s -> s.Replace("\"", "\\\"")
            | _ when FSharpType.IsRecord(x.GetType()) ->
                let propertyValues =
                    FSharpType.GetRecordFields(x.GetType())
                    |> Array.sortBy (fun fi -> fi.Name)
                    |> Array.map (fun fi -> fi.GetValue(x, null))

                propertyValues
                |> Array.map (inner nestingLevel maxDepth)
                |> fun arr -> "{ " + String.Join(", ", arr) + " }"
            | _ when isSeq x ->
                let seq = x :?> System.Collections.IEnumerable |> Seq.cast<_>
                let localMaxDepth = seqDepth 0 x
                let convertedSeq = seq |> Seq.map (inner (nestingLevel + 1) localMaxDepth)
                "{ " + String.Join(seqSeparator, convertedSeq) + " }"
            | _ -> $"{x}"

        let maxDepth = seqDepth 0 (box v)
        inner 0 maxDepth (box v)


    let getBrackets obj =
        match obj with
        | _ when isArray obj -> ("[|", "|]")
        | _ when isList obj -> ("[", "]")
        | _ -> ("{{", "}}")


    let getComplexSeparator obj =
        match obj with
        | _ when FSharpType.IsRecord(obj.GetType()) -> ""
        | _ when isArray obj -> ""
        | _ when isList obj -> ""
        | _ when isSeq obj -> ""
        | _ -> " "


    let toOutputString (x: obj) : string =
        let rec inner (x: obj) (indent: string) : string =
            match x with
            | null -> "null"
            | :? string as str -> $"\"{str}\""
            | _ when isTuple x ->
                let elements = FSharpValue.GetTupleFields(x) |> Array.map (fun el -> inner el "")
                $"""({String.Join(", ", elements)})"""
            | _ when isArray x || isList x || isSeq x ->
                let brackets = getBrackets x
                let elements = (x :?> System.Collections.IEnumerable) |> Seq.cast<obj> |> Seq.toList
                if List.forall isSimpleType elements then $""" {(fst brackets)} {String.Join("; ", elements)} {(snd brackets)}"""
                else
                    let newIndent = $"{baseIndent}{indent}"
                    let sb = StringBuilder()
                    sb.AppendLine() |> ignore
                    sb.Append($"{baseIndent}{indent}{(fst brackets)}") |> ignore
                    let formattedElems = elements |> List.map (fun el -> $"{inner el newIndent}")

                    if List.forall isDiscriminatedUnion elements
                    then
                        sb.AppendLine() |> ignore
                        sb.Append($"{baseIndent}{newIndent}") |> ignore
                        sb.AppendLine(String.Join($"{Nl}{baseIndent}{newIndent}", formattedElems)) |> ignore
                    else
                        sb.AppendLine(String.Join("", formattedElems)) |> ignore

                    sb.Append($"{baseIndent}{indent}{(snd brackets)}") |> ignore
                    sb.ToString()
            | _ when FSharpType.IsRecord(x.GetType()) ->
                let sb = StringBuilder()
                if indent <> "" then sb.AppendLine("") |> ignore
                let indent1 = if indent <> "" then $"{baseIndent}{indent}" else indent
                sb.AppendLine($"{indent1}{{") |> ignore

                let recordType = x.GetType()
                let fields = FSharpType.GetRecordFields(recordType)
                let newIndent = $"{baseIndent}{indent1}"

                for field in fields do
                    let fieldName = field.Name
                    let fieldValue = field.GetValue(x)
                    let separator = getComplexSeparator fieldValue
                    sb.AppendLine($"{newIndent}{fieldName} ={separator}{inner fieldValue newIndent}") |> ignore

                sb.Append($"{indent1}}}") |> ignore
                sb.ToString()
            | _ when FSharpType.IsUnion(x.GetType()) ->
                let unionType = x.GetType()
                let case, fields = FSharpValue.GetUnionFields(x, unionType)
                let caseName = case.Name
                let fieldStrs = fields |> Array.map (fun f -> inner f indent)
                let allFieldsStr = $"""{String.Join(" ", fieldStrs)}"""

                let sep =
                    if allFieldsStr <> "" && allFieldsStr.StartsWith " " |> not then " "
                    else ""

                $"""{caseName}{sep}{allFieldsStr}"""
            | _ -> x.ToString()

        inner x ""


    type WolframRequest =
        {
            wolframCode : WolframCode
            inputFileName : FileName
            outputFileName : FileName
        }


    let tryGetMathKernelFileName() =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let result = provider.getMathKernelFileName() |> Ok
            provider.save()
            result
        | Error e -> Error e


    let tryRunMathematicaScript (request: WolframRequest) =
        try
            match request.inputFileName.tryEnsureFolderExists(), request.outputFileName.tryEnsureFolderExists(), request.inputFileName.tryGetFullFileName(), request.outputFileName.tryGetFullFileName() with
            | Ok(), Ok(), Ok i, Ok (FileName o) ->
                // Write the content to the input file.
                File.WriteAllText(i.value, request.wolframCode.value)

                // Start the Wolfram Kernel with explicit link name.
                match tryGetMathKernelFileName() with
                | Ok kernelName ->
                    let rnd = Random()
                    let logId = rnd.Next()

                    let linkArgs = $"-linkname '{kernelName.value} -mathlink' -linklaunch"
                    // let linkArgs = $"-linkname '{kernelName.value} -mathlink -noprompt -noicon' -linklaunch -linkprotocol tcp"
                    // let linkArgs = $"-linkname '{kernelName.value} -mathlink -noprompt -noicon -logfile C:\\Temp\\mathkernel_{logId}.log' -linklaunch"

                    Logger.logTrace $"tryRunMathematicaScript - linkArgs: '%A{linkArgs}'."
                    let link = MathLinkFactory.CreateKernelLink(linkArgs)
                    Logger.logTrace $"tryRunMathematicaScript - link created."

                    try
                        // Discard the initial kernel output.
                        link.WaitAndDiscardAnswer()
                        Logger.logTrace $"tryRunMathematicaScript - call to link.WaitAndDiscardAnswer() completed."

                        // Load the .m or .wl file as a script and run it.
                        // Wolfram wants "\\\\" for each "\\" in the path. Don't ask why.
                        let scriptCommand = $"<< \"%s{i.toWolframNotation()}\"" // Use "<< file.m" to load the script.
                        Logger.logTrace $"tryRunMathematicaScript - scriptCommand: '%A{scriptCommand}'."
                        link.Evaluate(scriptCommand)
                        Logger.logTrace $"tryRunMathematicaScript - call to link.Evaluate(scriptCommand) completed."

                        // Wait for the result of the evaluation.
                        link.WaitForAnswer() |> ignore
                        Logger.logTrace "tryRunMathematicaScript - call to link.WaitForAnswer() completed."

                        // Check for the output file in the output folder
                        if File.Exists(o) then
                            // If the output file is found, read it as a byte array and return it as Ok.
                            let fileBytes = File.ReadAllBytes(o)
                            link.Close() // Close the link when done.
                            Logger.logTrace $"tryRunMathematicaScript: Completed successfully. Loaded {fileBytes.Length} bytes."
                            Ok fileBytes
                        else
                            // If the output file is not found, return an error.
                            link.Close()
                            Logger.logTrace $"tryRunMathematicaScript - call to link.Close() completed."
                            let message = $"Output file '{o}' is not found."
                            Logger.logError message
                            Error message
                    with
                    | ex ->
                        link.Close()
                        Logger.logTrace $"tryRunMathematicaScript - call to link.Close() completed."
                        let message = $"An error occurred during Wolfram evaluation: {ex.Message}"
                        Logger.logError message
                        Error message
                | Error e ->
                    let message = $"%A{e}"
                    Logger.logError message
                    Error message
            | _ ->
                let message = $"tryRunMathematicaScript failed for request: '%A{request}'."
                Logger.logCrit message
                failwith message
        with
        | ex ->
            let message = $"An error occurred: {ex.Message}"
            Logger.logError message
            Error message


    type ListLineParams =
        {
            frame : bool
            plotRange : PlotRange option
            gridLines : GridLines option
            imageSize : ImageSize option
            labelStyle : LabelStyle option
        }

        static member defaultValue =
            {
                frame = true
                plotRange = Some PlotRange.defaultValue
                gridLines = Some GridLines.defaultValue
                imageSize = Some ImageSize.defaultValue
                labelStyle = Some LabelStyle.defaultValue
            }


    let getListLinePlotData (o : FileName) (p : ListLineParams) (d : DataSeries2D array) =
        let legends = d |> Array.map _.dataLabel.value
        let xyData = d |> Array.mapi (fun i s -> $"xy{i} = {{" + (s.dataPoints |> List.map (fun p -> $"{{ {toWolframNotation p.x}, {toWolframNotation p.y} }}") |> joinStrings ", ") + $"}};") |> joinStrings Nl
        let xyVar = d |> Array.mapi (fun i _ -> $"xy{i}") |> joinStrings ", "
        let frame = if p.frame then ", Frame -> True" else ""
        let plotRange = p.plotRange |> Option.map (fun r -> $", PlotRange -> %A{r}") |> Option.defaultValue ""
        let gridLines = p.gridLines |> Option.map (fun g -> $", GridLines -> %A{g}") |> Option.defaultValue ""
        let imageSize = p.imageSize |> Option.map (fun i -> $", ImageSize -> %A{i}") |> Option.defaultValue ""
        let labelStyle = p.labelStyle |> Option.map (fun l -> $", LabelStyle -> {l.value}") |> Option.defaultValue ""
        let plotLegends = ", PlotLegends -> legends"

        let data =
            [
                xyData
                $"legends = {(toWolframNotation legends)};"
                $"outputFile = \"{o.toWolframNotation()}\";"
                $"Export[outputFile, ListLinePlot[{{{xyVar}}}{frame}{plotRange}{gridLines}{imageSize}{labelStyle}{plotLegends}], \"PNG\"];"
            ]
            |> joinStrings Nl
        data |> WolframCode


    let getListLinePlot i o p d =
        let data = getListLinePlotData o p d

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
                fileName = o
            }
            |> BinaryResult
            |> Some
        | Error e ->
            Logger.logError $"getListLinePlot - Error: %A{e}."
            None
