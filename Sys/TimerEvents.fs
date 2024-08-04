﻿namespace Softellect.Sys

open System.Threading
open System

open Softellect.Sys.Rop
open Softellect.Sys.Errors
open Softellect.Sys.Core
open Softellect.Sys.Logging

module TimerEvents =


    [<Literal>]
    //let RefreshInterval = 30_000
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
            logger : Logger<'E>
            toErr : TimerEventError -> 'E
        }


    type TimerEventHandlerInfo<'E> =
        {
            timerEventInfo : TimerEventInfo
            timerProxy : TimerEventProxy<'E>
        }

        member i.withFirstDelay d =
            { i with timerEventInfo = { i.timerEventInfo with firstDelay = d } }

        static member defaultValue logger e h n =
            {
                timerEventInfo = TimerEventInfo.defaultValue n
                timerProxy =
                    {
                        eventHandler = h
                        logger = logger
                        toErr = e
                    }
            }

        static member oneHourValue logger e h n =
            {
                timerEventInfo = TimerEventInfo.oneHourValue n
                timerProxy =
                    {
                        eventHandler = h
                        logger = logger
                        toErr = e
                    }
            }


    type TimerEventHandler<'E> (i : TimerEventHandlerInfo<'E>) =
        let mutable counter = -1
        let mutable lastStartedAt = DateTime.Now
        let handlerId = i.timerEventInfo.handlerId |> Option.defaultValue (Guid.NewGuid())
        let refreshInterval = i.timerEventInfo.refreshInterval |> Option.defaultValue RefreshInterval
        let logger = i.timerProxy.logger
        let firstDelay = i.timerEventInfo.firstDelay |> Option.defaultValue refreshInterval
        let logError e = e |> i.timerProxy.toErr |> logger.logErrorData
        let logWarn e = e |> i.timerProxy.toErr |> logger.logWarnData
        let info = $"TimerEventHandler: handlerId = %A{handlerId}, handlerName = %A{i.timerEventInfo.handlerName}"
//        do $"TimerEventHandler: %A{i}" |> logger.logDebugString

        let g() =
            try
                match i.timerProxy.eventHandler() with
                | Ok() ->
//                    logger.logDebugString "proxy.eventHandler() - succeeded."
                    ()
                | Error e -> logger.logErrorData e
            with
            | e ->
                {
                    handlerName = i.timerEventInfo.handlerName
                    handlerId = handlerId
                    unhandledException = e
                }
                |> UnhandledEventHandlerExn |> logError

        let eventHandler _ =
            try
//                logger.logDebugString $"eventHandler: %A{i}"

                if Interlocked.Increment(&counter) = 0
                then
                    lastStartedAt <- DateTime.Now
                    timedImplementation false logger info g
                else
                    {
                        handlerName = i.timerEventInfo.handlerName
                        handlerId = handlerId
                        runTime = DateTime.Now - lastStartedAt
                    }
                    |> StillRunningEventHandlerErr |> logWarn
            finally Interlocked.Decrement(&counter) |> ignore


        let timer = new Timer(TimerCallback(eventHandler), null, Timeout.Infinite, refreshInterval)

        member _.start() =
            try
                timer.Change(firstDelay, refreshInterval) |> ignore
            with
            | e ->
                {
                    handlerName = i.timerEventInfo.handlerName
                    handlerId = handlerId
                    unhandledException = e
                }
                |> UnhandledEventHandlerExn |> logError

        member _.stop() =
            try
                timer.Change(Timeout.Infinite, refreshInterval) |> ignore
            with
            | e ->
                {
                    handlerName = i.timerEventInfo.handlerName
                    handlerId = handlerId
                    unhandledException = e
                }
                |> UnhandledEventHandlerExn |> logError
