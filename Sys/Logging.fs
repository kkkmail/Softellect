namespace Softellect.Sys

open System.Diagnostics
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Log4Net.AspNetCore.Extensions
open log4net

module Logging =

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


    let configureLogging (logging: ILoggingBuilder) =
        logging.ClearProviders() |> ignore
        logging.AddConsole() |> ignore
        logging.AddDebug() |> ignore
        logging.SetMinimumLevel(LogLevel.Trace) |> ignore


    let configureServiceLogging (logging: ILoggingBuilder) =
        logging.ClearProviders() |> ignore
        logging.AddLog4Net("log4net.config") |> ignore
        logging.SetMinimumLevel(LogLevel.Trace) |> ignore

        let log4NetLogger = LogManager.GetLogger("log4net-default")

        Logger.configureLogger(fun level callerName message ->
            match level with
            | TraceLog -> log4NetLogger.Trace($"[{callerName}] {message}", null)
            | DebugLog -> log4NetLogger.Debug($"[{callerName}] {message}")
            | InfoLog -> log4NetLogger.Info($"[{callerName}] {message}")
            | WarnLog -> log4NetLogger.Warn($"[{callerName}] {message}")
            | ErrorLog -> log4NetLogger.Error($"[{callerName}] {message}")
            | CritLog -> log4NetLogger.Fatal($"[{callerName}] {message}")
            )
