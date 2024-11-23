namespace Softellect.Sys

open System.Diagnostics
open System.Runtime.CompilerServices
open System.Runtime.InteropServices

module Logging =

    // /// An encapsulation of a logger name.
    // type LoggerName =
    //     | LoggerName of string
    //
    //     member this.value = let (LoggerName v) = this in v
    //
    //
    // type LogLevel =
    //     | CritLog
    //     | ErrLog
    //     | WarnLog
    //     | InfoLog
    //     | DebugLog
    //
    //
    // type Logger =
    //     {
    //         logCrit : string -> unit
    //         logError : string -> unit
    //         logWarn : string -> unit
    //         logInfo : string -> unit
    //         logDebug : string -> unit
    //     }
    //
    //     member this.logIfError v =
    //         match v with
    //         | Ok _ -> ()
    //         | Error e -> this.logError $"%A{e}"
    //
    //         v
    //
    //     static member defaultValue =
    //         {
    //             logCrit = printfn "CRIT: %A, %A" DateTime.Now
    //             logError = printfn "ERROR: %A, %A" DateTime.Now
    //             logWarn = printfn "WARN: %A, %A" DateTime.Now
    //             logInfo = printfn "INFO: %A, %A" DateTime.Now
    //             logDebug = printfn "DEBUG: %A, %A" DateTime.Now
    //         }
    //
    //     static member releaseValue =
    //         {
    //             logCrit = printfn "CRIT: %A, %A" DateTime.Now
    //             logError = printfn "ERROR: %A, %A" DateTime.Now
    //             logWarn = printfn "WARN: %A, %A" DateTime.Now
    //             logInfo = printfn "INFO: %A, %A" DateTime.Now
    //             logDebug = fun _ -> ()
    //         }
    //
    //
    //     static member getCallerName([<Optional; DefaultParameterValue(false)>] ?addTimeStamp: bool, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?memberName: string) =
    //         let memberName = defaultArg memberName ""
    //         let a = defaultArg addTimeStamp false
    //
    //         if a then $"{memberName}__{DateTime.Now:yyyyMMdd_HHmm}"
    //         else $"{memberName}"
    //
    //
    // type GetLogger = LoggerName -> Logger

    // type LogLevel =
    //     | Debug
    //     | Info
    //     | Warning
    //     | Error
    //     | Critical
    //
    // type Logger private () =
    //     static let mutable logImpl: (LogLevel -> obj -> string -> unit) option =
    //         Some (fun level message callerName -> printfn $"[{level}] - {callerName}: %A{message}")
    //
    //     static member configureLogger (impl: LogLevel -> obj -> string -> unit) = logImpl <- Some impl
    //     static member disableLogging () = logImpl <- None
    //
    //     static member private log (level: LogLevel) (message: obj) (callerName: string) =
    //         match logImpl with
    //         | Some logger -> logger level message callerName
    //         | None -> ()
    //
    //     static member logDebug (message : obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
    //         Logger.log Debug message (defaultArg callerName "")
    //
    //     static member logInfo (message : obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
    //         Logger.log Info message (defaultArg callerName "")
    //
    //     static member logWarning (message : obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
    //         Logger.log Warning message (defaultArg callerName "")
    //
    //     static member logError (message : obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
    //         Logger.log Error message (defaultArg callerName "")
    //
    //     static member logCritical (message : obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
    //         Logger.log Critical message (defaultArg callerName "")

    // type LogLevel =
    //     | Debug
    //     | Info
    //     | Warning
    //     | Error
    //     | Critical
    //
    // type Logger private () =
    //     /// Default log implementation.
    //     static let mutable logImpl: (LogLevel -> obj -> string -> unit) option =
    //         Some (fun level message callerName -> printfn $"[{level}] - {callerName}: %A{message}")
    //
    //     /// Minimum log level for filtering messages.
    //     static let mutable minLogLevel = Debug
    //
    //     /// Configure the logger implementation.
    //     static member configureLogger (impl: LogLevel -> obj -> string -> unit) = logImpl <- Some impl
    //
    //     /// Disable logging completely.
    //     static member disableLogging () = logImpl <- None
    //
    //     /// Adjust the minimum log level (verbosity).
    //     static member setMinLogLevel level = minLogLevel <- level
    //
    //     /// Private log function with verbosity check.
    //     static member private log (level: LogLevel) (message: obj) (callerName: string) =
    //         if level >= minLogLevel then
    //             match logImpl with
    //             | Some logger -> logger level message callerName
    //             | None -> ()
    //
    //     static member logDebug (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
    //         Logger.log Debug message (defaultArg callerName "")
    //
    //     static member logInfo (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
    //         Logger.log Info message (defaultArg callerName "")
    //
    //     static member logWarning (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
    //         Logger.log Warning message (defaultArg callerName "")
    //
    //     static member logError (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
    //         Logger.log Error message (defaultArg callerName "")
    //
    //     static member logCritical (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
    //         Logger.log Critical message (defaultArg callerName "")

    type LogLevel =
        | TraceLog
        | DebugLog
        | InfoLog
        | WarnLog
        | ErrorLog
        | CritLog

        /// The right padded with spaces fixed length name to be used in standard logging.
        member l.logName =
            match l with
            | TraceLog -> "TRACE"
            | DebugLog -> "DEBUG"
            | InfoLog ->  "INFO "
            | WarnLog ->  "WARN "
            | ErrorLog -> "ERROR"
            | CritLog ->  "CRIT "

        /// A human-friendly value to be used in appsettings.json.
        member l.value =
            match l with
            | TraceLog -> "Trace"
            | DebugLog -> "Debug"
            | InfoLog ->  "Information"
            | WarnLog ->  "Warning"
            | ErrorLog -> "Error"
            | CritLog ->  "Critical"

        static member tryDeserialize (s : string) =
            match s.Trim() with
            | "Trace" -> TraceLog |> Some
            | "Debug" -> DebugLog |> Some
            | "Information" -> InfoLog |> Some
            | "Warning" -> WarnLog |> Some
            | "Error" -> ErrorLog |> Some
            | "Critical" -> CritLog |> Some
            | _ -> None

        static member defaultValue = DebugLog


    type Logger private () =
        static let stopwatch = Stopwatch.StartNew()

        /// Default log implementation.
        static let mutable logImpl: LogLevel -> obj -> string -> unit =
            fun level message callerName ->
                let elapsedSeconds = double stopwatch.ElapsedMilliseconds / 1_000.0
                printfn $"#{elapsedSeconds,9:F3} # {level.logName} # {callerName} # %A{message}"

        /// Minimum log level for filtering messages.
        static let mutable minLogLevel = DebugLog

        /// Logging enabled/disabled flag.
        static let mutable isLoggingEnabled = true

        /// Configure the logger implementation.
        static member configureLogger (impl: LogLevel -> obj -> string -> unit) = logImpl <- impl

        /// Enable logging.
        static member enableLogging () = isLoggingEnabled <- true

        /// Disable logging.
        static member disableLogging () = isLoggingEnabled <- false

        /// Adjust the minimum log level (verbosity).
        static member setMinLogLevel level = minLogLevel <- level

        /// Private log function with verbosity and enable/disable checks.
        static member private log (level: LogLevel) (message: obj) (callerName: string) =
            if isLoggingEnabled && level >= minLogLevel then logImpl level message callerName

        static member logTrace (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log TraceLog message (defaultArg callerName "")

        static member logDebug (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log DebugLog message (defaultArg callerName "")

        static member logInfo (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log InfoLog message (defaultArg callerName "")

        static member logWarn (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log WarnLog message (defaultArg callerName "")

        static member logError (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log ErrorLog message (defaultArg callerName "")

        static member logCrit (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log CritLog message (defaultArg callerName "")


// /// Convenience functions for direct usage.
// module Log =
//     let logTrace = Logging.Logger.logTrace
//     let logDebug = Logging.Logger.logDebug
//     let logInfo = Logging.Logger.logInfo
//     let logWarn = Logging.Logger.logWarn
//     let logError = Logging.Logger.logError
//     let logCrit = Logging.Logger.logCrit
