namespace Softellect.Sys

open System

/// Collection of general errors & related functionality.
module GeneralErrors =

    type ErrorId =
        | ErrorId of Guid

        static member getNewId() = Guid.NewGuid() |> ErrorId
        member this.value = let (ErrorId v) = this in v


    //type FileError =
    //    | GeneralFileExn of exn
    //    | GetFolderNameExn of exn
    //    | GetFileNameExn of exn
    //    | FileNotFoundErr of string
    //    | ReadFileExn of exn
    //    | WriteFileExn of exn
    //    | DeleteFileExn of exn
    //    | GetObjectIdsExn of exn
    //    | CreateChartsExn of exn
    //    | SaveChartsExn of exn


    type JsonParseError =
        | InvalidStructureErr of string


    type SerializationError =
        | SerializationExn of exn
        | DeserializationExn of exn


    //type ServiceInstallerError =
    //    | InstallServiceErr of exn
    //    | UninstallServiceErr of exn
    //    | StartServiceErr of exn
    //    | StopServiceErr of exn


    type GeneralError =
        | JsonParseErr of JsonParseError
        | SerializationErr of SerializationError
