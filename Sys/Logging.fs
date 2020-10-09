namespace Softellect.Sys

open Softellect.Sys.Errors

module Logging =

    type LogData<'E> =
        | SimpleLogData of string
        | ErrLogData of Err<'E>

        member data.map f =
            match data with
            | SimpleLogData s -> SimpleLogData s
            | ErrLogData d -> f d |> ErrLogData


    type Logger<'E> =
        {
            logCrit : LogData<'E> -> unit
            logError : LogData<'E> -> unit
            logWarn : LogData<'E> -> unit
            logInfo : LogData<'E> -> unit
        }

        member this.logInfoString (s : string) = s |> SimpleLogData |> this.logInfo
        member this.logErrData e = e |> ErrLogData |> this.logError
        member this.logWarnData e = e |> ErrLogData |> this.logWarn
        member this.logInfoData e = e |> ErrLogData |> this.logInfo

        member this.logIfError v =
            match v with
            | Ok _ -> ignore()
            | Error e -> e |> ErrLogData |> this.logError

            v

        static member defaultValue : Logger<'E> =
            {
                logCrit = printfn "CRIT: %A"
                logError = printfn "ERROR: %A"
                logWarn = printfn "WARN: %A"
                logInfo = printfn "INFO: %A"
            }

        member log.map f =
            {
                logCrit = fun e -> e.map f |> log.logCrit
                logError = fun e -> e.map f |> log.logError
                logWarn = fun e -> e.map f |> log.logWarn
                logInfo = fun e -> e.map f |> log.logInfo
            }
