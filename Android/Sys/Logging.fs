namespace Softellect.Sys

open System
open System.Diagnostics
open System.Runtime.CompilerServices
open System.Runtime.InteropServices

/// Android-specific logging module that provides the same interface as the Windows version.
module Logging =

    type ProjectName =
        | ProjectName of string

        member this.value = let (ProjectName v) = this in v
        static member defaultValue = ProjectName "Default"


    type LogLevel =
        | TraceLog
        | DebugLog
        | InfoLog
        | WarnLog
        | ErrorLog
        | CritLog

        member l.logName =
            match l with
            | TraceLog -> "TRACE"
            | DebugLog -> "DEBUG"
            | InfoLog ->  "INFO "
            | WarnLog ->  "WARN "
            | ErrorLog -> "ERROR"
            | CritLog ->  "CRIT "

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
        static let logGate = obj()

        static let mutable logImpl: LogLevel -> obj -> string -> unit =
            fun level message callerName ->
                let elapsedSeconds = double stopwatch.ElapsedMilliseconds / 1_000.0
                let ts = DateTime.Now
                let s = ts.ToString("yyyyMMdd_HHmmss.fff")
                let line = $"# {s} #{elapsedSeconds,9:F3} # {level.logName} # {callerName} # %A{message}"
                lock logGate (fun () -> Console.WriteLine(line))

        static let mutable minLogLevel = DebugLog
        static let mutable isLoggingEnabled = true

        static member configureLogger impl = logImpl <- impl
        static member enableLogging () = isLoggingEnabled <- true
        static member disableLogging () = isLoggingEnabled <- false
        static member setMinLogLevel level = minLogLevel <- level
        static member shouldLog level = isLoggingEnabled && level >= minLogLevel

        static member private log (level: LogLevel) (message: obj) (callerName: string) =
            if Logger.shouldLog level then logImpl level message callerName

        static member logTrace (getMessage: unit -> obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            if Logger.shouldLog TraceLog then logImpl TraceLog (getMessage()) (defaultArg callerName "")

        static member logTraceIf (logWhenTrue: unit -> bool, getMessage: unit -> obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            if Logger.shouldLog TraceLog then
                if logWhenTrue() then logImpl TraceLog (getMessage()) (defaultArg callerName "")

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
