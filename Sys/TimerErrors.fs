namespace Softellect.Sys

open System

module TimerErrors =

    type EventHandlerError =
        | UnhandledEventHandlerExn of string * Guid * exn
        | StillRunningEventHandlerErr of string * Guid * DateTime


