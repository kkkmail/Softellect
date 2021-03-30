namespace Softellect.Sys

open System

module TimerErrors =

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
