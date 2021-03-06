﻿namespace Softellect.Sys

open System.Threading
open System

open Softellect.Sys.Rop
open Softellect.Sys.TimerErrors
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


    type TimerEventHandler<'E> (i : TimerEventInfo, proxy : TimerEventProxy<'E>) =
        let mutable counter = -1
        let handlerId = i.handlerId |> Option.defaultValue (Guid.NewGuid())
        let refreshInterval = i.refreshInterval |> Option.defaultValue RefreshInterval
        let logger = proxy.logger
        let firstDelay = i.firstDelay |> Option.defaultValue refreshInterval
        let logError e = e |> proxy.toErr |> logger.logErrorData
        let logWarn e = e |> proxy.toErr |> logger.logWarnData
        let info = sprintf "TimerEventHandler: handlerId = %A, handlerName = %A" handlerId i.handlerName
        do sprintf "TimerEventHandler: %A" i |> logger.logDebugString

        let g() =
            try
                match proxy.eventHandler() with
                | Ok() ->
                    logger.logDebugString "proxy.eventHandler() - succeeded."
                    ignore()
                | Error e ->
                    sprintf "proxy.eventHandler() - Error: %A" e |> logger.logDebugString
                    proxy.logger.logErrorData e
            with
            | e -> (i.handlerName, handlerId, e) |> UnhandledEventHandlerExn |> logError

        let eventHandler _ =
            try
                sprintf "eventHandler: %A" i |> logger.logDebugString
                proxy.logger.logInfoString info
                if Interlocked.Increment(&counter) = 0
                then timedImplementation false proxy.logger info g
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
