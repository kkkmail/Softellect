namespace Softellect.Sys

open System
open System.Net
open System.IO
open System.Text.RegularExpressions

/// Collection of various primitive abstractions.
module Primitives =

    [<Literal>]
    let CopyrightInfo = "MIT License - Copyright Konstantin K. Konstantinov and Alisa F. Konstantinova © 2015 - 2024."


    [<Literal>]
    let DefaultRootDrive = "C"


    /// String.Empty is not a const.
    [<Literal>]
    let EmptyString = ""


    /// Environment.NewLine is too long, and it is not a const.
    [<Literal>]
    let Nl = "\r\n"


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


    /// An encapsulation of a folder name.
    type FolderName =
        | FolderName of string

        member this.value = let (FolderName v) = this in v
        member this.combine (subFolder : FolderName) = Path.Combine(this.value, subFolder.value) |> FolderName

        static member tryCreate (s: string) : Result<FolderName, string> =
            let invalidChars = Path.GetInvalidPathChars()

            // Regex for three or more consecutive dots
            let consecutiveDots = Regex(@"\.{3,}")

            // List of reserved names in Windows (e.g., "CON", "PRN", etc.)
            let reservedNames =
                [ "CON"; "PRN"; "AUX"; "NUL"; "COM1"; "COM2"; "COM3"; "COM4"; "COM5"; "COM6"; "COM7"; "COM8"; "COM9"; "LPT1"; "LPT2"; "LPT3"; "LPT4"; "LPT5"; "LPT6"; "LPT7"; "LPT8"; "LPT9" ]

            // Function to check if the name is a reserved Windows name
            let isReservedName (name: string) =
                reservedNames |> List.exists (fun reserved -> String.Equals(name, reserved, StringComparison.OrdinalIgnoreCase))

            // Check for invalid conditions
            if String.IsNullOrWhiteSpace(s) then
                Error $"Folder name cannot be empty or whitespace."
            elif s.IndexOfAny(invalidChars) >= 0 then
                let invalidInFolder = s.ToCharArray() |> Array.filter (fun c -> invalidChars |> Array.contains c)
                Error $"Folder name contains invalid characters: {String(invalidInFolder)}"
            elif s.Contains("\\\\") then
                // Disallow multiple consecutive backslashes
                Error $"Folder name cannot contain consecutive backslashes (\\\\)."
            elif consecutiveDots.IsMatch(s) then
                // Disallow more than two consecutive dots
                Error $"Folder name cannot contain more than two consecutive dots (...)."
            elif isReservedName s then
                // Disallow reserved names
                let r = String.Join(", ", reservedNames)
                Error $"Folder name cannot be a reserved name (e.g., {r})."
            else
                Ok (FolderName s)

        static member defaultResultLocation = FolderName "C:\\Results"
        static member defaultSolverLocation = FolderName "C:\\Solvers"
        static member defaultSolverOutputLocation = FolderName "C:\\Temp"


    /// File extensions used in the system.
    type FileExtension =
        | FileExtension of string

        member this.value = let (FileExtension v) = this in v
        member this.toWolframNotation() = this.value.Replace(".", "").ToUpper()

        //| PNG
        //| HTML

        //member e.extension =
        //    match e with
        //    | PNG -> ".png"
        //    | HTML -> ".html"

        //static member tryCreate (s : string) =
        //    match s.ToLower() with
        //        | ".png" -> PNG |> Some
        //        | ".html" -> HTML |> Some
        //        | _ -> None


    /// An encapsulation of a file name.
    type FileName =
        | FileName of string

        member this.value = let (FileName v) = this in v
        member this.combine (subFolder : FolderName) = Path.Combine(subFolder.value, this.value) |> FileName
        member this.addExtension(FileExtension extension) =
            if extension.StartsWith "." then this.value + extension |> FileName
            else failwith this.value + "." + extension |> FileName

        static member tryCreate (s: string) = FileName s |> Ok

        /// Wolfram path needs to double "\".
        member this.toWolframNotation() = this.value.Replace("\\", "\\\\")

        member this.tryGetExtension() =
            try
                Path.GetExtension(this.value) |> FileExtension |> Some
            with
            | e ->
                printfn $"FileName.tryGetExtension - Exception: %A{e}."
                None


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
            | BinaryFormat -> ".bin"
            | BinaryZippedFormat -> ".binz"
            | JSonFormat -> ".json"
            | XmlFormat -> ".xml"
            |> FileExtension


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


    type TextResult =
        {
            textContent: string
            fileName : FileName
        }


    type BinaryResult =
        {
            binaryContent: byte[]
            fileName : FileName
        }


    type CalculationResult =
        | TextResult of TextResult
        | BinaryResult of BinaryResult

        member c.fileName =
            match c with
            | TextResult h -> h.fileName
            | BinaryResult b -> b.fileName


    let appSettingsFile = FileName "appsettings.json"


    type EncryptionType =
        | AES
        | RSA

        static member defaultValue = AES

        member e.value = $"%A{e}"

        static member create (s : string) =
            match s.ToUpper() with
            | "AES" -> AES
            | "RSA" -> RSA
            | _ -> EncryptionType.defaultValue


    type KeyId =
        | KeyId of Guid

        member this.value = let (KeyId v) = this in v


    type PublicKey =
        | PublicKey of string

        member this.value = let (PublicKey v) = this in v


    type PrivateKey =
        | PrivateKey of string

        member this.value = let (PrivateKey v) = this in v


    type ProjectName =
        | ProjectName of string

        member this.value = let (ProjectName v) = this in v
        static member defaultValue = ProjectName "Default"

    type MonitorResolution =
        {
            monitorWidth : int
            monitorHeight : int
        }

        static member fullHD =
            {
                monitorWidth = 1920
                monitorHeight = 1080
            }

        static member fourK =
            {
                monitorWidth = 3840
                monitorHeight = 2160
            }


    type MonitorDpi =
        {
            dpiX : int
            dpiY : int
        }


    type ColorDepth =
        | ColorDepth of int

        member this.value = let (ColorDepth v) = this in v
