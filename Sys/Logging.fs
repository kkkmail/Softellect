namespace Softellect.Sys

open System
open System.Runtime.CompilerServices
open System.Runtime.InteropServices

/// TODO kk:20240824 - Needs reworking as it is clumsy and subsequently it is not used at all.
module Logging =

    /// An encapsulation of a logger name.
    type LoggerName =
        | LoggerName of string

        member this.value = let (LoggerName v) = this in v


    //type LogData<'E> =
    //    | SimpleLogData of string
    //    | ErrLogData of 'E


    type LogLevel =
        | CritLog
        | ErrLog
        | WarnLog
        | InfoLog
        | DebugLog


    //type LogMessage<'E> =
    //    {
    //        logLevel : LogLevel
    //        logData : LogData<'E>
    //    }


    //type Logger<'E> =
    //    {
    //        logCrit : LogData<'E> -> unit
    //        logError : LogData<'E> -> unit
    //        logWarn : LogData<'E> -> unit
    //        logInfo : LogData<'E> -> unit
    //        logDebug : LogData<'E> -> unit
    //    }

    //    member this.logCritString s = s |> SimpleLogData |> this.logCrit
    //    member this.logErrorString s = s |> SimpleLogData |> this.logError
    //    member this.logWarnString s = s |> SimpleLogData |> this.logWarn
    //    member this.logInfoString s = s |> SimpleLogData |> this.logInfo
    //    member this.logDebugString s = s |> SimpleLogData |> this.logDebug

    //    member this.logCritData e = e |> ErrLogData |> this.logCrit
    //    member this.logErrorData e = e |> ErrLogData |> this.logError
    //    member this.logWarnData e = e |> ErrLogData |> this.logWarn
    //    member this.logInfoData e = e |> ErrLogData |> this.logInfo
    //    member this.logDebugData e = e |> ErrLogData |> this.logDebug

    //    member this.logIfError v =
    //        match v with
    //        | Ok _ -> ()
    //        | Error e -> this.logErrorData e

    //        v

    //    static member defaultValue : Logger<'E> =
    //        {
    //            logCrit = printfn "CRIT: %A, %A" DateTime.Now
    //            logError = printfn "ERROR: %A, %A" DateTime.Now
    //            logWarn = printfn "WARN: %A, %A" DateTime.Now
    //            logInfo = printfn "INFO: %A, %A" DateTime.Now
    //            logDebug = printfn "DEBUG: %A, %A" DateTime.Now
    //        }

    //    static member releaseValue : Logger<'E> =
    //        {
    //            logCrit = printfn "CRIT: %A, %A" DateTime.Now
    //            logError = printfn "ERROR: %A, %A" DateTime.Now
    //            logWarn = printfn "WARN: %A, %A" DateTime.Now
    //            logInfo = printfn "INFO: %A, %A" DateTime.Now
    //            logDebug = fun _ -> ()
    //        }

    type Logger =
        {
            logCrit : string -> unit
            logError : string -> unit
            logWarn : string -> unit
            logInfo : string -> unit
            logDebug : string -> unit
        }

        member this.logIfError v =
            match v with
            | Ok _ -> ()
            | Error e -> this.logError $"%A{e}"

            v

        static member defaultValue =
            {
                logCrit = printfn "CRIT: %A, %A" DateTime.Now
                logError = printfn "ERROR: %A, %A" DateTime.Now
                logWarn = printfn "WARN: %A, %A" DateTime.Now
                logInfo = printfn "INFO: %A, %A" DateTime.Now
                logDebug = printfn "DEBUG: %A, %A" DateTime.Now
            }

        static member releaseValue =
            {
                logCrit = printfn "CRIT: %A, %A" DateTime.Now
                logError = printfn "ERROR: %A, %A" DateTime.Now
                logWarn = printfn "WARN: %A, %A" DateTime.Now
                logInfo = printfn "INFO: %A, %A" DateTime.Now
                logDebug = fun _ -> ()
            }


        static member getCallerName([<Optional; DefaultParameterValue(false)>] ?addTimeStamp: bool, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?memberName: string) =
            let memberName = defaultArg memberName ""
            let a = defaultArg addTimeStamp false

            if a then $"{memberName}__{DateTime.Now:yyyyMMdd_HHmm}"
            else $"{memberName}"


    type GetLogger = LoggerName -> Logger
