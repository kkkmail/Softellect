namespace Softellect.Analytics

open Softellect.Sys.AppSettings
open Softellect.Sys.Primitives

module AppSettings =
    let private mathKernelFileNameKey = ConfigKey "MathKernelFileName"
    let private wolframInputFolderKey = ConfigKey "WolframInput"
    let private wolframOutputFolderKey = ConfigKey "WolframOutput"


    let private defaultMathKernelFileName = "C:\\Program Files\\Wolfram Research\\Mathematica\\13.0\\mathkernel.exe" |> FileName
    let private defaultWolframInputFolder = "C:\\Wolfram\\Input\\" |> FolderName
    let private defaultWolframOutputFolder = "C:\\Wolfram\\Output\\" |> FolderName


    type AppSettingsProvider
        with
        member this.getMathKernelFileName() = this.getFileNameOrDefault mathKernelFileNameKey defaultMathKernelFileName
        member this.getWolframInputFolder() = this.getFolderNameOrDefault wolframInputFolderKey defaultWolframInputFolder
        member this.getWolframOutputFolder() = this.getFolderNameOrDefault wolframOutputFolderKey defaultWolframOutputFolder
