namespace Softellect.Sys

open System.Diagnostics
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Log4Net.AspNetCore.Extensions
open log4net

module Logging =

    let private projectNameProperty = "ProjectName"
    let private log4netConfig = "log4net.config"
    let private log4netDefaultLogName = "log4net-default"


    type ProjectName =
        | ProjectName of string

        member this.value = let (ProjectName v) = this in v
        static member defaultValue = ProjectName "Default"


    let configureProjectName (ProjectName projectName) =
        let globalContext = GlobalContext.Properties
        globalContext[projectNameProperty] <- projectName


    let private tryConfigureProjectName po =
        match po with
        | Some p -> configureProjectName p
        | None -> ()


    /// Configure a default subfolder for logs.
    let private dummy = configureProjectName ProjectName.defaultValue


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

        /// A Microsoft.Extensions.Logging.LogLevel value.
        member l.logLevel =
            match l with
            | TraceLog -> LogLevel.Trace
            | DebugLog -> LogLevel.Debug
            | InfoLog ->  LogLevel.Information
            | WarnLog ->  LogLevel.Warning
            | ErrorLog -> LogLevel.Error
            | CritLog ->  LogLevel.Critical

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
        static member configureLogger impl = logImpl <- impl

        /// Enable logging.
        static member enableLogging () = isLoggingEnabled <- true

        /// Disable logging.
        static member disableLogging () = isLoggingEnabled <- false

        /// Adjust the minimum log level (verbosity).
        static member setMinLogLevel level = minLogLevel <- level

        static member private shouldLog level = isLoggingEnabled && level >= minLogLevel

        /// Private log function with verbosity and enable/disable checks.
        static member private log (level: LogLevel) (message: obj) (callerName: string) =
            if Logger.shouldLog level then logImpl level message callerName

        /// Function logTrace is the only one that takes a function to get the message,
        /// because we don't want to evaluate the message if the trace log is not applicable.
        static member logTrace (getMessage: unit -> obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            if Logger.shouldLog TraceLog then logImpl TraceLog (getMessage()) (defaultArg callerName "")

        static member logDebug (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log DebugLog message (defaultArg callerName "")

        static member logInfo (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log InfoLog message (defaultArg callerName "")

        static member logWarn (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log WarnLog message (defaultArg callerName "")

        static member logError (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log ErrorLog message (defaultArg callerName "")

        static member logIfError (result, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            match result with
            | Ok _ -> ()
            | Error e -> Logger.log ErrorLog $"%A{e}" (defaultArg callerName "")

            result

        static member logCrit (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log CritLog message (defaultArg callerName "")

        static member logCritIfError (result, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            match result with
            | Ok _ -> ()
            | Error e -> Logger.log CritLog $"%A{e}" (defaultArg callerName "")

            result


    let configureLogging po (logging: ILoggingBuilder) =
        tryConfigureProjectName po
        logging.ClearProviders() |> ignore
        logging.AddConsole() |> ignore
        logging.AddDebug() |> ignore
        logging.SetMinimumLevel(LogLevel.Trace) |> ignore


    let configureServiceLogging po (logging: ILoggingBuilder) =
        tryConfigureProjectName po
        logging.ClearProviders() |> ignore
        logging.AddLog4Net(log4netConfig) |> ignore
        logging.SetMinimumLevel(LogLevel.Trace) |> ignore

        let log4NetLogger = LogManager.GetLogger(log4netDefaultLogName)

        Logger.configureLogger (fun level message callerName ->
            let m = $"{(callerName.PadRight(30))} # {message}"

            match level with
            | TraceLog -> log4NetLogger.Trace(m, null)
            | DebugLog -> log4NetLogger.Debug(m)
            | InfoLog -> log4NetLogger.Info(m)
            | WarnLog -> log4NetLogger.Warn(m)
            | ErrorLog -> log4NetLogger.Error(m)
            | CritLog -> log4NetLogger.Fatal(m)
            )
