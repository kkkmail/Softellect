namespace Softellect.Sys

open System

module TimerErrors =

    type TimerEventError =
        | UnhandledEventHandlerExn of string * Guid * exn
        | StillRunningEventHandlerErr of string * Guid * DateTime
