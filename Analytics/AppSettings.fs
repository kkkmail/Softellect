namespace Softellect.Analytics

open Softellect.Sys.AppSettings
open Softellect.Sys.Logging
open Softellect.Sys.Primitives

module AppSettings =
    let private mathKernelFileNameKey = ConfigKey "MathKernelFileName"
    let private wolframInputFolderKey = ConfigKey "WolframInput"
    let private wolframOutputFolderKey = ConfigKey "WolframOutput"


    // let private defaultMathKernelFileName = "C:\\Program Files\\Wolfram Research\\Mathematica\\13.0\\mathkernel.exe" |> FileName
    // let private defaultMathKernelFileName = "C:\\Program Files\\Wolfram Research\\Mathematica\\13.0\\math.exe" |> FileName
    let private defaultMathKernelFileName = "C:\\Program Files\\Wolfram Research\\Wolfram\\14.1\\math.exe" |> FileName
    let private defaultWolframInputFolder = "C:\\Wolfram\\Input\\" |> FolderName
    let private defaultWolframOutputFolder = "C:\\Wolfram\\Output\\" |> FolderName


    type WolframParams =
        {
            mathKernel : FileName
            wolframInputFolder : FolderName
            wolframOutputFolder : FolderName
        }


    type AppSettingsProvider
        with
        member this.getMathKernelFileName() = this.getFileNameOrDefault mathKernelFileNameKey defaultMathKernelFileName
        member this.getWolframInputFolder() = this.getFolderNameOrDefault wolframInputFolderKey defaultWolframInputFolder
        member this.getWolframOutputFolder() = this.getFolderNameOrDefault wolframOutputFolderKey defaultWolframOutputFolder

        member this.getWolframParams() =
            {
                mathKernel = this.getMathKernelFileName()
                wolframInputFolder = this.getWolframInputFolder()
                wolframOutputFolder = this.getWolframOutputFolder()
            }


    let private loadWolframParamsImpl save =
        match AppSettingsProvider.tryCreate() with
        | Ok appSettingsProvider ->
            let w = appSettingsProvider.getWolframParams()

            if save then
                Logger.logTrace $"loadWolframParams - w = %A{w}."
                match appSettingsProvider.trySave() with
                | Ok () -> Logger.logTrace $"loadWolframParams - saved."
                | Error e -> Logger.logError $"loadWolframParams - error: %A{e}."
            w
        | Error e ->
            Logger.logError $"loadWolframParams - error: %A{e}."

            {
                mathKernel = defaultMathKernelFileName
                wolframInputFolder = defaultWolframInputFolder
                wolframOutputFolder = defaultWolframOutputFolder
            }

    let private wolframParams = Lazy<WolframParams>(fun () -> loadWolframParamsImpl false)
    let loadWolframParams() = wolframParams.Value
    let saveWolframParams() = loadWolframParamsImpl true
