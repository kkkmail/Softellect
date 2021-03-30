namespace Softellect.Sys

open System

module Logging =

    type LogData<'E> =
        | SimpleLogData of string
        | ErrLogData of 'E


    type LogLevel =
        | CritLog
        | ErrLog
        | WarnLog
        | InfoLog
        | DebugLog


    type LogMessage<'E> =
        {
            logLevel : LogLevel
            logData : LogData<'E>
        }


    type Logger<'E> =
        {
            logCrit : LogData<'E> -> unit
            logError : LogData<'E> -> unit
            logWarn : LogData<'E> -> unit
            logInfo : LogData<'E> -> unit
            logDebug : LogData<'E> -> unit
        }

        member this.logCritString s = s |> SimpleLogData |> this.logCrit
        member this.logErrorString s = s |> SimpleLogData |> this.logError
        member this.logWarnString s = s |> SimpleLogData |> this.logWarn
        member this.logInfoString s = s |> SimpleLogData |> this.logInfo
        member this.logDebugString s = s |> SimpleLogData |> this.logDebug

        member this.logCritData e = e |> ErrLogData |> this.logCrit
        member this.logErrorData e = e |> ErrLogData |> this.logError
        member this.logWarnData e = e |> ErrLogData |> this.logWarn
        member this.logInfoData e = e |> ErrLogData |> this.logInfo
        member this.logDebugData e = e |> ErrLogData |> this.logDebug

        member this.logIfError v =
            match v with
            | Ok _ -> ()
            | Error e -> this.logErrorData e

            v

        static member defaultValue : Logger<'E> =
            {
                logCrit = printfn "CRIT: %A, %A" DateTime.Now
                logError = printfn "ERROR: %A, %A" DateTime.Now
                logWarn = printfn "WARN: %A, %A" DateTime.Now
                logInfo = printfn "INFO: %A, %A" DateTime.Now
                logDebug = printfn "DEBUG: %A, %A" DateTime.Now
            }

        static member releaseValue : Logger<'E> =
            {
                logCrit = printfn "CRIT: %A, %A" DateTime.Now
                logError = printfn "ERROR: %A, %A" DateTime.Now
                logWarn = printfn "WARN: %A, %A" DateTime.Now
                logInfo = printfn "INFO: %A, %A" DateTime.Now
                logDebug = fun _ -> ()
            }
