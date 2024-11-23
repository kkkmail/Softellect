namespace Softellect.Sys

open System.Threading
open System

open Softellect.Sys.Rop
open Softellect.Sys.Errors
open Softellect.Sys.Core
open Softellect.Sys.Logging

module TimerEvents =


    [<Literal>]
    let RefreshInterval = 10_000


    [<Literal>]
    let OneHourRefreshInterval = 3_600_000


    type TimerEventInfo =
        {
            handlerId : Guid option
            handlerName : string
            refreshInterval : int option
            firstDelay : int option
        }

        static member defaultValue n =
            {
                handlerId = None
                handlerName = n
                refreshInterval = None
                firstDelay = None
            }

        static member oneHourValue n =
            {
                handlerId = None
                handlerName = n
                refreshInterval = Some OneHourRefreshInterval
                firstDelay = None
            }


    type TimerEventProxy<'E> =
        {
            eventHandler : unit -> UnitResult<'E>
            toErr : TimerEventError -> 'E
        }


    type TimerEventHandlerInfo<'E> =
        {
            timerEventInfo : TimerEventInfo
            timerProxy : TimerEventProxy<'E>
        }

        member i.withFirstDelay d =
            { i with timerEventInfo = { i.timerEventInfo with firstDelay = d } }

        static member defaultValue e h n =
            {
                timerEventInfo = TimerEventInfo.defaultValue n
                timerProxy =
                    {
                        eventHandler = h
                        toErr = e
                    }
            }

        static member oneHourValue e h n =
            {
                timerEventInfo = TimerEventInfo.oneHourValue n
                timerProxy =
                    {
                        eventHandler = h
                        toErr = e
                    }
            }


    type TimerEventHandler<'E> (i0 : TimerEventHandlerInfo<'E>) =
        let mutable counter = -1
        let mutable lastStartedAt = DateTime.Now
        let handlerId = i0.timerEventInfo.handlerId |> Option.defaultValue (Guid.NewGuid())
        let refreshInterval = i0.timerEventInfo.refreshInterval |> Option.defaultValue RefreshInterval
        let firstDelay = i0.timerEventInfo.firstDelay |> Option.defaultValue refreshInterval
        let i = { i0 with timerEventInfo.handlerId = Some handlerId; timerEventInfo.refreshInterval = Some refreshInterval; timerEventInfo.firstDelay = Some firstDelay }
        let info = $"TimerEventHandler: handlerId = %A{handlerId}, handlerName = %A{i.timerEventInfo.handlerName}"

        let g() =
            try
                match i.timerProxy.eventHandler() with
                | Ok() -> Logger.logTrace $"TimerEventHandler: %A{i.timerEventInfo} - succeeded."
                | Error e -> Logger.logError $"%A{i.timerEventInfo} - FAILED, error: %A{e}"
            with
            | e ->
                {
                    handlerName = i.timerEventInfo.handlerName
                    handlerId = handlerId
                    unhandledException = e
                }
                |> UnhandledEventHandlerExn |> Logger.logError

        let eventHandler _ =
            try
                if Interlocked.Increment(&counter) = 0
                then
                    lastStartedAt <- DateTime.Now
                    timedImplementation false info g
                else
                    {
                        handlerName = i.timerEventInfo.handlerName
                        handlerId = handlerId
                        runTime = DateTime.Now - lastStartedAt
                    }
                    |> StillRunningEventHandlerErr |> Logger.logWarn
            finally Interlocked.Decrement(&counter) |> ignore


        let timer = new Timer(TimerCallback(eventHandler), null, Timeout.Infinite, refreshInterval)

        member _.start() =
            try
                Logger.logInfo $"TimerEventHandler: %A{i.timerEventInfo} - starting timer."
                timer.Change(firstDelay, refreshInterval) |> ignore
            with
            | e ->
                {
                    handlerName = i.timerEventInfo.handlerName
                    handlerId = handlerId
                    unhandledException = e
                }
                |> UnhandledEventHandlerExn |> Logger.logError

        member _.stop() =
            try
                Logger.logInfo $"TimerEventHandler: %A{i.timerEventInfo} - stopping timer."
                timer.Change(Timeout.Infinite, refreshInterval) |> ignore
            with
            | e ->
                {
                    handlerName = i.timerEventInfo.handlerName
                    handlerId = handlerId
                    unhandledException = e
                }
                |> UnhandledEventHandlerExn |> Logger.logError
