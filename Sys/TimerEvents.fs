namespace Softellect.Sys

open System.Threading
open System

open Softellect.Sys.Primitives
open Softellect.Sys.Rop
open Softellect.Sys.Errors
open Softellect.Sys.Core
open Softellect.Sys.Logging

module TimerEvents =

    type TimerEventInfo =
        {
            handlerId : Guid option
            handlerName : string
            refreshInterval : TimerRefreshInterval option
            firstDelay : TimerRefreshInterval option
        }

        static member defaultValue n =
            {
                handlerId = None
                handlerName = n
                refreshInterval = None
                firstDelay = None
            }

        static member oneMinuteValue n =
            {
                handlerId = None
                handlerName = n
                refreshInterval = Some TimerRefreshInterval.oneMinuteValue
                firstDelay = None
            }

        static member oneHourValue n =
            {
                handlerId = None
                handlerName = n
                refreshInterval = Some TimerRefreshInterval.oneHourValue
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

        static member oneMinuteValue e h n =
            {
                timerEventInfo = TimerEventInfo.oneMinuteValue n
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
        let refreshInterval = i0.timerEventInfo.refreshInterval |> Option.defaultValue TimerRefreshInterval.defaultValue
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


        let timer = new Timer(TimerCallback(eventHandler), null, Timeout.Infinite, int refreshInterval.value)

        member _.start() =
            try
                Logger.logInfo $"TimerEventHandler: %A{i.timerEventInfo} - starting timer."
                timer.Change(int firstDelay.value, int refreshInterval.value) |> ignore
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
                timer.Change(Timeout.Infinite, int refreshInterval.value) |> ignore
            with
            | e ->
                {
                    handlerName = i.timerEventInfo.handlerName
                    handlerId = handlerId
                    unhandledException = e
                }
                |> UnhandledEventHandlerExn |> Logger.logError
