namespace Softellect.Analytics

open System
open Microsoft.FSharp.Reflection
open System.Text
open Softellect.Sys.AppSettings
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open System.IO
open Wolfram.NETLink
open Softellect.Analytics.AppSettings

/// A collection of functions to convert F# objects to Wolfram Language notation.
module Wolfram =

    //. An encapsulation of Wolfram code file content.
    type WolframCode =
        | WolframCode of string

        member this.value = let (WolframCode v) = this in v


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
                    let linkArgs = $"-linkname '{kernelName.value} -mathlink' -linklaunch"
                    printfn $"tryRunMathematicaScript - linkArgs: '%A{linkArgs}'."
                    let link = MathLinkFactory.CreateKernelLink(linkArgs)

                    try
                        // Discard the initial kernel output.
                        link.WaitAndDiscardAnswer()

                        // Load the .m or .wl file as a script and run it.
                        // Wolfram wants "\\\\" for each "\\" in the path. Don't ask why.
                        let scriptCommand = $"<< \"%s{i.toWolframNotation()}\"" // Use "<< file.m" to load the script.
                        printfn $"tryRunMathematicaScript - scriptCommand: '%A{scriptCommand}'."
                        link.Evaluate(scriptCommand)

                        // Wait for the result of the evaluation.
                        link.WaitForAnswer() |> ignore
                        printfn "tryRunMathematicaScript - call to link.WaitForAnswer() completed."

                        // Check for the output file in the output folder
                        if File.Exists(o) then
                            // If the output file is found, read it as a byte array and return it as Ok.
                            let fileBytes = File.ReadAllBytes(o)
                            link.Close() // Close the link when done.
                            Ok fileBytes
                        else
                            // If the output file is not found, return an error.
                            link.Close()
                            Error $"Output file '{o}' is not found."
                    with
                    | ex ->
                        link.Close()
                        Error $"An error occurred during Wolfram evaluation: {ex.Message}"
                | Error e -> Error $"%A{e}"
            | _ -> failwith $"tryRunMathematicaScript failed for request: '%A{request}'."
        with
        | ex -> Error $"An error occurred: {ex.Message}"
