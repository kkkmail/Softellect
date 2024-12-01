namespace Softellect.Sys

open System
open System.Diagnostics
open System.IO
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Log4Net.AspNetCore.Extensions
open log4net
open Softellect.Sys.Primitives
open log4net.Appender
open log4net.Config
open log4net.Layout
open log4net.Repository.Hierarchy

module Logging =

    let private projectNameProperty = "ProjectName"


    /// See:
    ///     https://stackoverflow.com/questions/278761/is-there-a-net-framework-method-for-converting-file-uris-to-paths-with-drive-le
    ///     https://stackoverflow.com/questions/837488/how-can-i-get-the-applications-path-in-a-net-console-application
    ///     https://stackoverflow.com/questions/52797/how-do-i-get-the-path-of-the-assembly-the-code-is-in
    let getAssemblyLocation() =
        let x = Uri(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)).LocalPath
        FolderName x


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

        static member logIfError (result, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            match result with
            | Ok _ -> ()
            | Error e -> Logger.log ErrorLog $"%A{e}" (defaultArg callerName "")

            result

        static member logCrit (message: obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            Logger.log CritLog message (defaultArg callerName "")


    let configureLogging po (logging: ILoggingBuilder) =
        tryConfigureProjectName po
        logging.ClearProviders() |> ignore
        logging.AddConsole() |> ignore
        logging.AddDebug() |> ignore
        logging.SetMinimumLevel(LogLevel.Trace) |> ignore


    let configureServiceLogging po (logging: ILoggingBuilder) =
        tryConfigureProjectName po
        logging.ClearProviders() |> ignore
        logging.AddLog4Net("log4net.config") |> ignore
        logging.SetMinimumLevel(LogLevel.Trace) |> ignore

        let log4NetLogger = LogManager.GetLogger("log4net-default")

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

    // Does not work: https://chatgpt.com/c/67410229-b9f4-8009-a394-3bde2fd2eb07
    // let configureServiceLogging po (logging: ILoggingBuilder) =
    //     tryConfigureProjectName po
    //     logging.ClearProviders() |> ignore
    //     logging.AddLog4Net("log4net.config") |> ignore
    //     logging.SetMinimumLevel(LogLevel.Trace) |> ignore
    //
    //     let log4NetLogger, eo =
    //         try
    //             // Load log4net.config from the assembly location
    //             let assemblyLocation = getAssemblyLocation()
    //             let configPath = Path.Combine(assemblyLocation.value, "log4net.config")
    //             let hierarchy = LogManager.GetRepository() :?> Hierarchy
    //             XmlConfigurator.Configure(hierarchy, FileInfo(configPath)) |> ignore
    //
    //             // Clone the existing RollingFileAppender and override its layout for F#
    //             let fsharpAppender =
    //                 match hierarchy.Root.Appenders |> Seq.tryFind (fun app -> app :? RollingFileAppender) with
    //                 | Some appender ->
    //                     let rollingAppender = appender :?> RollingFileAppender
    //                     let newAppender = new RollingFileAppender()
    //                     newAppender.Name <- "FSharpRollingFileAppender"
    //                     newAppender.File <- rollingAppender.File
    //                     newAppender.AppendToFile <- rollingAppender.AppendToFile
    //                     newAppender.RollingStyle <- rollingAppender.RollingStyle
    //                     newAppender.DatePattern <- rollingAppender.DatePattern
    //                     newAppender.StaticLogFileName <- rollingAppender.StaticLogFileName
    //
    //                     // Override the layout for F# logs
    //                     let fsharpLayout = new PatternLayout()
    //                     fsharpLayout.ConversionPattern <- "# %date{yyyy-MM-dd HH:mm:ss.fff} # %-5level # %message %newline%newline"
    //                     fsharpLayout.ActivateOptions()
    //                     newAppender.Layout <- fsharpLayout
    //
    //                     newAppender.ActivateOptions()
    //                     newAppender
    //                 | None -> failwith "RollingFileAppender not found in log4net.config"
    //
    //             // Create a new logger for F#
    //             let fsharpLoggerName = "FSharpLogger"
    //             let fsharpLogger = hierarchy.LoggerFactory.CreateLogger(hierarchy, fsharpLoggerName)
    //             fsharpLogger.AddAppender(fsharpAppender)
    //             fsharpLogger.Level <- log4net.Core.Level.All
    //             fsharpLogger.Repository.Configured <- true
    //
    //             // Use the new F# logger
    //             let logger = LogManager.GetLogger(fsharpLoggerName)
    //             logger, None
    //         with
    //         | e ->
    //             let defaultLogger = LogManager.GetLogger("log4net-default")
    //             defaultLogger, Some e
    //
    //
    //     Logger.configureLogger(fun level message callerName ->
    //         let formattedMessage = $"{(callerName.PadRight(30))} # {message}"
    //
    //         match level with
    //         | TraceLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Trace, formattedMessage, null)
    //         | DebugLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Debug, formattedMessage, null)
    //         | InfoLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Info, formattedMessage, null)
    //         | WarnLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Warn, formattedMessage, null)
    //         | ErrorLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Error, formattedMessage, null)
    //         | CritLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Fatal, formattedMessage, null)
    //     )
    //
    //     match eo with
    //     | Some e -> Logger.logCrit $"%A{e}"
    //     | None -> ()

    // let configureServiceLogging po (logging: ILoggingBuilder) =
    //     tryConfigureProjectName po
    //     logging.ClearProviders() |> ignore
    //     logging.AddLog4Net("log4net.config") |> ignore
    //     logging.SetMinimumLevel(LogLevel.Trace) |> ignore
    //
    //     let log4NetLogger, eo =
    //         try
    //             // Load log4net.config from the assembly location
    //             let assemblyLocation = getAssemblyLocation()
    //             let configPath = Path.Combine(assemblyLocation.value, "log4net.config")
    //             let hierarchy = LogManager.GetRepository() :?> Hierarchy
    //             XmlConfigurator.Configure(hierarchy, FileInfo(configPath)) |> ignore
    //
    //             // Clone the existing RollingFileAppender and override its layout for F#
    //             let fsharpAppender =
    //                 match hierarchy.Root.Appenders |> Seq.tryFind (fun app -> app :? RollingFileAppender) with
    //                 | Some appender ->
    //                     let rollingAppender = appender :?> RollingFileAppender
    //
    //                     // Create a new RollingFileAppender with the same properties
    //                     let newAppender = new RollingFileAppender()
    //                     newAppender.Name <- "FSharpRollingFileAppender"
    //                     newAppender.File <- rollingAppender.File // Use the same file location
    //                     newAppender.AppendToFile <- rollingAppender.AppendToFile
    //                     newAppender.RollingStyle <- rollingAppender.RollingStyle
    //                     newAppender.DatePattern <- rollingAppender.DatePattern
    //                     newAppender.StaticLogFileName <- rollingAppender.StaticLogFileName
    //
    //                     // Override the layout for F# logs
    //                     let fsharpLayout = new PatternLayout()
    //                     fsharpLayout.ConversionPattern <- "# %date{yyyy-MM-dd HH:mm:ss.fff} # %-5level # %message %newline%newline"
    //                     fsharpLayout.ActivateOptions()
    //                     newAppender.Layout <- fsharpLayout
    //
    //                     newAppender.ActivateOptions()
    //                     newAppender
    //                 | None -> failwith "RollingFileAppender not found in log4net.config"
    //
    //             // Create a new logger for F#
    //             let fsharpLoggerName = "FSharpLogger"
    //             let fsharpLogger = hierarchy.LoggerFactory.CreateLogger(hierarchy, fsharpLoggerName)
    //             fsharpLogger.AddAppender(fsharpAppender)
    //             fsharpLogger.Level <- log4net.Core.Level.All
    //
    //             // Use the new F# logger
    //             let logger = LogManager.GetLogger(fsharpLoggerName)
    //             logger, None
    //         with
    //         | e ->
    //             let defaultLogger = LogManager.GetLogger("log4net-default")
    //             defaultLogger, Some e
    //
    //     // Configure the F# Logger for use
    //     Logger.configureLogger(fun level message callerName ->
    //         let formattedMessage = $"{(callerName.PadRight(30))} # {message}"
    //
    //         match level with
    //         | TraceLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Trace, formattedMessage, null)
    //         | DebugLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Debug, formattedMessage, null)
    //         | InfoLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Info, formattedMessage, null)
    //         | WarnLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Warn, formattedMessage, null)
    //         | ErrorLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Error, formattedMessage, null)
    //         | CritLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Fatal, formattedMessage, null)
    //     )
    //
    //     // Log any configuration error
    //     match eo with
    //     | Some e -> Logger.logCrit $"%A{e}"
    //     | None -> ()


    // let configureServiceLogging po (logging: ILoggingBuilder) =
    //     tryConfigureProjectName po
    //     logging.ClearProviders() |> ignore
    //     logging.AddLog4Net("log4net.config") |> ignore
    //     logging.SetMinimumLevel(LogLevel.Trace) |> ignore
    //
    //     let log4NetLogger, eo =
    //         try
    //             // Load log4net.config from the assembly location
    //             let assemblyLocation = getAssemblyLocation()
    //             let configPath = Path.Combine(assemblyLocation.value, "log4net.config")
    //             let hierarchy = LogManager.GetRepository() :?> Hierarchy
    //             XmlConfigurator.Configure(hierarchy, FileInfo(configPath)) |> ignore
    //
    //             // Clone the existing RollingFileAppender and override its layout for F#
    //             let fsharpAppender =
    //                 match hierarchy.Root.Appenders |> Seq.tryFind (fun app -> app :? RollingFileAppender) with
    //                 | Some appender ->
    //                     let rollingAppender = appender :?> RollingFileAppender
    //
    //                     // Create a new RollingFileAppender with the same properties
    //                     let newAppender = new RollingFileAppender()
    //                     newAppender.Name <- "FSharpRollingFileAppender"
    //                     newAppender.File <- rollingAppender.File // Use the same base file
    //                     newAppender.AppendToFile <- rollingAppender.AppendToFile
    //                     newAppender.RollingStyle <- rollingAppender.RollingStyle
    //                     newAppender.DatePattern <- rollingAppender.DatePattern
    //                     newAppender.StaticLogFileName <- rollingAppender.StaticLogFileName
    //
    //                     // Override the layout for F# logs
    //                     let fsharpLayout = new PatternLayout()
    //                     fsharpLayout.ConversionPattern <- "# %date{yyyy-MM-dd HH:mm:ss.fff} # %-5level # %message %newline%newline"
    //                     fsharpLayout.ActivateOptions()
    //                     newAppender.Layout <- fsharpLayout
    //
    //                     // Activate and return the appender
    //                     newAppender.ActivateOptions()
    //                     newAppender
    //                 | None -> failwith "RollingFileAppender not found in log4net.config"
    //
    //             // Create a new logger for F#
    //             let fsharpLoggerName = "FSharpLogger"
    //             let fsharpLogger = hierarchy.LoggerFactory.CreateLogger(hierarchy, fsharpLoggerName)
    //             fsharpLogger.AddAppender(fsharpAppender)
    //             fsharpLogger.Level <- log4net.Core.Level.All
    //
    //             // Activate the repository to ensure it picks up changes
    //             hierarchy.Configured <- true
    //
    //             // Use the new F# logger
    //             let logger = LogManager.GetLogger(fsharpLoggerName)
    //             logger, None
    //         with
    //         | e ->
    //             let defaultLogger = LogManager.GetLogger("log4net-default")
    //             defaultLogger, Some e
    //
    //     // Configure the F# Logger for use
    //     Logger.configureLogger(fun level message callerName ->
    //         let formattedMessage = $"{(callerName.PadRight(30))} # {message}"
    //
    //         match level with
    //         | TraceLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Trace, formattedMessage, null)
    //         | DebugLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Debug, formattedMessage, null)
    //         | InfoLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Info, formattedMessage, null)
    //         | WarnLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Warn, formattedMessage, null)
    //         | ErrorLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Error, formattedMessage, null)
    //         | CritLog -> log4NetLogger.Logger.Log(null, log4net.Core.Level.Fatal, formattedMessage, null)
    //     )
    //
    //     // Log any configuration error
    //     match eo with
    //     | Some e -> Logger.logCrit $"%A{e}"
    //     | None -> ()
