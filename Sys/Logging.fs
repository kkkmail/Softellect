namespace Softellect.Sys

open Softellect.Sys.Errors

module Logging =

    type Logger<'E> =
        {
            logError : Err<'E> -> unit
            logWarn : Err<'E> -> unit
            logInfo : string -> unit
        }

        //member this.logInfoString (s : string) = ClmInfo.create s |> this.logInfo
        //member this.logExn s e = this.logError (UnhandledExn (s, e))
        member this.logInfoString (s : string) = this.logInfo s

        member this.logIfError v =
            match v with
            | Ok _ -> ignore()
            | Error e -> this.logError e

            v


        static member defaultValue : Logger<'E> =
            {
                logError = printfn "ERROR: %A"
                logWarn = printfn "WARN: %A"
                logInfo = printfn "INFO: %A"
            }
