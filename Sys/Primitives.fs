﻿namespace Softellect.Sys

open System
open System.Net

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


    [<Measure>] type millisecond
    [<Measure>] type second
    [<Measure>] type minute
    [<Measure>] type hour


    let millisecondsPerSecond = 1_000<millisecond/second>
    let secondsPerMinute = 60<second/minute>
    let minutesPerHour = 60<minute/hour>


    /// IPAddress cannot be serialized by FsPicler.
    /// Extend when needed.
    type IpAddress =
        | Ip4 of string
        | Ip6 of string // Not really supported now.

        member a.value =
            match a with
            | Ip4 v -> v
            | Ip6 v -> v

        member a.ipAddress = a.value |> IPAddress.Parse

        static member tryCreate (s : string) =
            match IPAddress.TryParse s with
            | (true, ipAddress) -> ipAddress.ToString() |> Ip4 |> Some
            | _ -> None


    let localHost = Ip4 "127.0.0.1"


    type VersionNumber =
        | VersionNumber of string

        member this.value = let (VersionNumber v) = this in v


    type ServiceAddress =
        | ServiceAddress of IpAddress

        member this.value = let (ServiceAddress v) = this in v
        static member tryCreate s = IpAddress.tryCreate s |> Option.map ServiceAddress

        member a.serialize() = $"{a.value.value}"
        static member tryDeserialize s = ServiceAddress.tryCreate s


    type ServicePort =
        | ServicePort of int

        member this.value = let (ServicePort v) = this in v
        member a.serialize() = $"{a.value}"

        static member tryDeserialize (s : string) = 
            match Int32.TryParse s with
            | (true, i) -> i |> ServicePort |> Some
            | _ -> None


    type ServiceName =
        | ServiceName of string

        member this.value = let (ServiceName v) = this in v
        member a.serialize() = $"{a.value}"
        static member tryDeserialize (s : string) = ServiceName s |> Some


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


    type BinaryChart =
        {
            binaryContent: byte[]
            fileName : string
        }


    type Chart =
        | HtmlChart of HtmlChart
        | BinaryChart of BinaryChart
