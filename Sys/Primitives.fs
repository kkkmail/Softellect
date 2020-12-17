namespace Softellect.Sys

/// Collection of various primitive abstractions.
module Primitives =

    [<Literal>]
    let DefaultRootDrive = "C"


    /// String.Empty is not a const.
    [<Literal>]
    let EmptyString = ""


    /// Environment.NewLine is too long and it is not a const.
    [<Literal>]
    let Nl = "\r\n"


    [<Measure>] type millisecond
    [<Measure>] type second
    [<Measure>] type minute
    [<Measure>] type hour


    let millisecondsPerSecond = 1_000<millisecond/second>
    let secondsPerMinute = 60<second/minute>
    let minutesPerHour = 60<minute/hour>


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
