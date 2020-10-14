namespace Softellect.Sys

open System
open Softellect.Sys.Errors

module Logging =

    type LogData<'E> =
        | SimpleLogData of DateTime * string
        | ErrLogData of DateTime * Err<'E>

        member data.map f =
            match data with
            | SimpleLogData (t, s) -> SimpleLogData (t, s)
            | ErrLogData (t, d) -> ErrLogData (t, f d)


    type Logger<'E> =
        {
            logCrit : LogData<'E> -> unit
            logError : LogData<'E> -> unit
            logWarn : LogData<'E> -> unit
            logInfo : LogData<'E> -> unit
        }

        member this.logInfoString (s : string) = (DateTime.Now, s) |> SimpleLogData |> this.logInfo
        member this.logErrData e = (DateTime.Now, e) |> ErrLogData |> this.logError
        member this.logWarnData e = (DateTime.Now, e) |> ErrLogData |> this.logWarn
        member this.logInfoData e = (DateTime.Now, e) |> ErrLogData |> this.logInfo

        member this.logIfError v =
            match v with
            | Ok _ -> ignore()
            | Error e -> this.logErrData e

            v

        static member defaultValue : Logger<'E> =
            {
                logCrit = printfn "CRIT: %A, %A" DateTime.Now
                logError = printfn "ERROR: %A, %A" DateTime.Now
                logWarn = printfn "WARN: %A, %A" DateTime.Now
                logInfo = printfn "INFO: %A, %A" DateTime.Now
            }

        member log.map f =
            {
                logCrit = fun e -> e.map f |> log.logCrit
                logError = fun e -> e.map f |> log.logError
                logWarn = fun e -> e.map f |> log.logWarn
                logInfo = fun e -> e.map f |> log.logInfo
            }
