namespace Softellect.Sys

open System

/// Collection of general errors & related functionality.
module Errors =

    type ErrorId =
        | ErrorId of Guid

        static member getNewId() = Guid.NewGuid() |> ErrorId
        member this.value = let (ErrorId v) = this in v


    type JsonParseError =
        | InvalidStructureErr of string


    type SerializationError =
        | SerializationExn of exn
        | DeserializationExn of exn


    type ServiceInstallerError =
        | InstallServiceErr of exn
        | UninstallServiceErr of exn
        | StartServiceErr of exn
        | StopServiceErr of exn


    type GeneralError =
        | JsonParseErr of JsonParseError
        | SerializationErr of SerializationError


    type DbError =
        | DbExn of exn


    // Timer Errors and related data.


    type UnhandledEventInfo =
        {
            handlerName : string
            handlerId : Guid
            unhandledException: exn
        }


    type LongRunningEventInfo =
        {
            handlerName : string
            handlerId : Guid
            runTime : TimeSpan
        }


    type TimerEventError =
        | UnhandledEventHandlerExn of UnhandledEventInfo
        | StillRunningEventHandlerErr of LongRunningEventInfo
