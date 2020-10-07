namespace Softellect.Sys

open System.Threading
open System

open Softellect.Sys.Errors
open Softellect.Sys.TimerErrors
open Softellect.Sys.Core
open Softellect.Sys.Logging
open Softellect.Sys.TimerErrors

module TimerEvents =


    [<Literal>]
    let RefreshInterval = 30_000


    [<Literal>]
    let OneHourRefreshInterval = 3_600_000


    type TimerUnitResult = UnitResult<TimerEventError>
    type TimerLogger = Logger<TimerEventError>


    type TimerEventInfo =
        {
            handlerId : Guid option
            handlerName : string
            eventHandler : unit -> TimerUnitResult
            refreshInterval : int option
            firstDelay : int option
            logger : TimerLogger
        }

        static member defaultValue logger h n =
            {
                handlerId = None
                handlerName = n
                eventHandler = h
                refreshInterval = None
                firstDelay = None
                logger = logger
            }

        static member oneHourValue logger h n =
            {
                handlerId = None
                handlerName = n
                eventHandler = h
                refreshInterval = Some OneHourRefreshInterval
                firstDelay = None
                logger = logger
            }


    type TimerEventHandler<'E> (i : TimerEventInfo) =
        let mutable counter = -1
        let handlerId = i.handlerId |> Option.defaultValue (Guid.NewGuid())
        let refreshInterval = i.refreshInterval |> Option.defaultValue RefreshInterval
        let firstDelay = i.firstDelay |> Option.defaultValue refreshInterval
        let logError e = e |> SingleErr |> i.logger.logErrData
        let logWarn e = e |> SingleErr |> i.logger.logWarnData
        let info = sprintf "TimerEventHandler: handlerId = %A, handlerName = %A" handlerId i.handlerName

        let g() =
            try
                match i.eventHandler() with
                | Ok() -> ignore()
                | Error e -> i.logger.logErrData e
            with
            | e -> (i.handlerName, handlerId, e) |> UnhandledEventHandlerExn |> logError

        let eventHandler _ =
            try
                i.logger.logInfoString info
                if Interlocked.Increment(&counter) = 0
                then timedImplementation false i.logger info g
                else (i.handlerName, handlerId, DateTime.Now) |> StillRunningEventHandlerErr |> logWarn
            finally Interlocked.Decrement(&counter) |> ignore


        let timer = new System.Threading.Timer(TimerCallback(eventHandler), null, Timeout.Infinite, refreshInterval)

        member _.start() =
            try
                timer.Change(firstDelay, refreshInterval) |> ignore
            with
            | e -> (i.handlerName, handlerId, e) |> UnhandledEventHandlerExn |> logError

        member _.stop() =
            try
                timer.Change(Timeout.Infinite, refreshInterval) |> ignore
            with
            | e -> (i.handlerName, handlerId, e) |> UnhandledEventHandlerExn |> logError
