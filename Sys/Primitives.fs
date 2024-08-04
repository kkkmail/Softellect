namespace Softellect.Sys

open System

/// Collection of various primitive abstractions.
module Primitives =

    [<Literal>]
    let CopyrightInfo = "MIT License - Copyright Konstantin K. Konstantinov and Alisa F. Konstantinova © 2015 - 2024."


    [<Literal>]
    let DefaultRootDrive = "C"


    /// String.Empty is not a const.
    [<Literal>]
    let EmptyString = ""


    /// Environment.NewLine is too long and it is not a const.
    [<Literal>]
    let Nl = "\r\n"


    [<Literal>]
    let AppSettingsFile = "appsettings.json"


    [<Literal>]
    let LocalHost = "127.0.0.1"


    [<Measure>] type millisecond
    [<Measure>] type second
    [<Measure>] type minute
    [<Measure>] type hour


    let millisecondsPerSecond = 1_000<millisecond/second>
    let secondsPerMinute = 60<second/minute>
    let minutesPerHour = 60<minute/hour>


    type VersionNumber =
        | VersionNumber of string

        member this.value = let (VersionNumber v) = this in v


    type ServiceAddress =
        | ServiceAddress of string

        member this.value = let (ServiceAddress v) = this in v


    type ServicePort =
        | ServicePort of int

        member this.value = let (ServicePort v) = this in v


    type ServiceName =
        | ServiceName of string

        member this.value = let (ServiceName v) = this in v


    type ConnectionString =
        | ConnectionString of string

        member this.value = let (ConnectionString v) = this in v


    type SqliteConnectionString =
        | SqliteConnectionString of string

        member this.value = let (SqliteConnectionString v) = this in v


    type SerializationFormat =
        | BinaryFormat
        | BinaryZippedFormat
        | JSonFormat
        | XmlFormat

        member format.fileExtension =
            match format with
            | BinaryFormat -> "bin"
            | BinaryZippedFormat -> "binz"
            | JSonFormat -> "json"
            | XmlFormat -> "xml"


    type RunQueueId =
        | RunQueueId of Guid

        member this.value = let (RunQueueId v) = this in v
        static member getNewId() = Guid.NewGuid() |> RunQueueId


    type RunQueueStatus =
        | NotStartedRunQueue
        | InactiveRunQueue
        | RunRequestedRunQueue
        | InProgressRunQueue
        | CompletedRunQueue
        | FailedRunQueue
        | CancelRequestedRunQueue
        | CancelledRunQueue

        member r.value =
            match r with
            | NotStartedRunQueue -> 0
            | InactiveRunQueue -> 1
            | RunRequestedRunQueue -> 7
            | InProgressRunQueue -> 2
            | CompletedRunQueue -> 3
            | FailedRunQueue -> 4
            | CancelRequestedRunQueue -> 5
            | CancelledRunQueue -> 6

        static member tryCreate i =
            match i with
            | 0 -> Some NotStartedRunQueue
            | 1 -> Some InactiveRunQueue
            | 7 -> Some RunRequestedRunQueue
            | 2 -> Some InProgressRunQueue
            | 3 -> Some CompletedRunQueue
            | 4 -> Some FailedRunQueue
            | 5 -> Some CancelRequestedRunQueue
            | 6 -> Some CancelledRunQueue
            | _ -> None


    type ProcessId =
        | ProcessId of int

        member this.value = let (ProcessId v) = this in v


    type HtmlChart =
        {
            htmlContent: string
            fileName : string
        }
