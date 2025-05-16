namespace Softellect.Analytics

open System.Text
open Softellect.Sys.Errors
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open System.IO

/// A collection of functions to control FFMpeg.
module FFMpeg =

    /// Clip duration in seconds.
    type ClipDuration =
        | ClipDuration of double

        member this.value = let (ClipDuration v) = this in v


    /// Type to describe the FFMpeg parameters.
    type AnimationData =
        {
            filePrefix : string
            framesFolder: FolderName
            frameExtension : FileExtension
            outputFolder : FolderName
            animationExtension : FileExtension
            tempFolder : FolderName
            clipDuration : ClipDuration
            ffmpegExecutable : FileName
        }


    let getFrameFiles (d : AnimationData) =
        try
            let files = Directory.GetFiles(d.framesFolder.value, d.filePrefix + "*" + d.frameExtension.value)

            files
            |> Array.toList
            |> List.sort
            |> List.map FileName
            |> Ok
        with
        | e ->
            Logger.logError $"getFrameFiles - error: %A{e}."
            FileError.GeneralFileExn e |> Error


    let getConcatFileName (d : AnimationData) =
        try
            match (FileName (Path.Join(d.tempFolder.value, d.filePrefix + "concat.txt"))).tryGetFullFileName() with
            | Ok fileName ->
                match fileName.tryEnsureFolderExists() with
                | Ok () -> Ok fileName
                | Error e -> Error e
            | Error e -> Error e
        with
        | e ->
            Logger.logError $"getConcatFileName - error: %A{e}."
            FileError.GeneralFileExn e |> Error


    let writeConcatFile (d : AnimationData) (files : FileName list) =
        match getConcatFileName d with
        | Ok concatFileName ->
            try
                let sb = StringBuilder()
                let normalize (FileName s) = s.Replace("\\","\\\\") // Need to escape backslashes for ffmpeg.
                sb.AppendLine("ffconcat version 1.0") |> ignore
                for file in files do sb.AppendLine($"file {(normalize file)}") |> ignore
                File.WriteAllText(concatFileName.value, sb.ToString())
                Ok concatFileName
            with
            | e ->
                Logger.logError $"writeConcatFile - error: %A{e}."
                FileError.GeneralFileExn e |> Error
        | Error e -> Error e


    let getOutputFileName (d : AnimationData) =
        try
            match (FileName (Path.Join(d.outputFolder.value, d.filePrefix + "animation" + d.animationExtension.value))).tryGetFullFileName() with
            | Ok fileName ->
                match fileName.tryEnsureFolderExists() with
                | Ok () -> Ok fileName
                | Error e -> Error e
            | Error e -> Error e
        with
        | e ->
            Logger.logError $"getOutputFileName - error: %A{e}."
            FileError.GeneralFileExn e |> Error


    let createAnimation (d : AnimationData) =
        let files = getFrameFiles d
        let toError e = e |> FileErr |> Error

        match files with
        | Ok f ->
            match writeConcatFile d f with
            | Ok concatFileName ->
                match getOutputFileName d with
                | Ok outputFile ->
                    let frameRate = (double f.Length) / d.clipDuration.value

                    let ffmpegArgs =
                        [
                            $"-y -f concat -safe 0 -r {frameRate}"
                            $"-i {concatFileName.value}"
                            $"-framerate {frameRate}"
                            outputFile.value
                        ]
                        |> joinStrings " "

                    match tryExecuteFile d.ffmpegExecutable ffmpegArgs with
                    | Ok r ->
                        Logger.logInfo $"createAnimation - ffmpeg result: %A{r}."

                        match r with
                        | 0 -> Ok outputFile
                        | _ ->
                            Logger.logError $"createAnimation - ffmpeg error: %A{r}."
                            FFMpegCallErr r |> FFMpegErr |> Error
                    | Error e ->
                        Logger.logError $"createAnimation - error: %A{e}."
                        toError e
                | Error e ->
                    Logger.logError $"createAnimation - error: %A{e}."
                    toError e
            | Error e ->
                Logger.logError $"createAnimation - error: %A{e}."
                toError e
        | Error e ->
            Logger.logError $"createAnimation - error: %A{e}."
            toError e
