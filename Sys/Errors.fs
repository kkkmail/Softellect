namespace Softellect.Sys

open System
open Softellect.Sys.Primitives

/// Collection of general errors & related functionality.
module Errors =

    type ErrorMessage =
        | ErrorMessage of string

        member this.value = let (ErrorMessage v) = this in v


    type ErrorId =
        | ErrorId of Guid

        static member getNewId() = Guid.NewGuid() |> ErrorId
        member this.value = let (ErrorId v) = this in v


    type JsonParseError =
        | InvalidStructureErr of string


    type SerializationError =
        | SerializationExn of exn
        | DeserializationExn of exn


    type ServiceInstallerError =
        | InstallServiceErr of exn
        | UninstallServiceErr of exn
        | StartServiceErr of exn
        | StopServiceErr of exn


    type DbError =
        | DbExn of exn


    type FileError =
        | ExecuteFileExn of exn
        | ExecuteFileErr of int * string
        | GeneralFileExn of exn
        | GetFolderNameExn of exn
        | GetFileNameExn of exn
        | FileNotFoundErr of FileName
        | ReadFileExn of exn
        | WriteFileExn of exn
        | DeleteFileExn of FileName * exn
        | DeleteFolderExn of FolderName * exn
        | DeleteFolderErr of FolderName
        | GetObjectIdsExn of exn
        | TryEnsureFolderExistsExn of exn
        | TryGetFullFileNameExn of exn
        | TryGetFolderNameExn of exn
        | TryGetExtensionExn of exn
        | TryOpenJsonExn of exn
        | JsonObjectIsNull of FileName


    type UnhandledEventInfo =
        {
            handlerName : string
            handlerId : Guid
            unhandledException: exn
        }


    type LongRunningEventInfo =
        {
            handlerName : string
            handlerId : Guid
            runTime : TimeSpan
        }


    type TimerEventError =
        | UnhandledEventHandlerExn of UnhandledEventInfo
        | StillRunningEventHandlerErr of LongRunningEventInfo


    type CryptoError =
        | SignDataExn of exn
        | TryEncryptAesExn of exn
        | TryDecryptAesExn of exn
        | TryEncryptRsaExn of exn
        | TryDecryptRsaExn of exn
        | VerifySignatureExn of exn
        | VerifySignatureFailedError
        | KeyFileExistErr of FileName
        | MissingKeyId
        | KeyExportExn of exn
        | KeyMismatchErr of (KeyId * FileName)
        | KeyImportExn of exn
        | KeyImportFileErr of FileError
        | KeyImportMissingIdErr


    type WindowsApiError =
        | WindowsApiExn of exn
        | WindowsApiCallErr of string
        | WindowsApiDisallowedOperationErr of string


    type FFMpegError =
        | FFMpegExn of exn
        | FFMpegCallErr of int


    type SysError =
        | AggregateErr of List<SysError>
        | JsonParseErr of JsonParseError
        | SerializationErr of SerializationError
        | ServiceInstallerErr of ServiceInstallerError
        | DbErr of DbError
        | FileErr of FileError
        | TimerEventErr of TimerEventError
        | CryptoErr of CryptoError
        | WindowsApiErr of WindowsApiError
        | FFMpegErr of FFMpegError

        static member addError a b =
            match a, b with
            | AggregateErr x, AggregateErr y -> AggregateErr (x @ y)
            | AggregateErr x, _ -> AggregateErr (x @ [ b ])
            | _, AggregateErr y -> AggregateErr ([ a ] @ y)
            | _ -> AggregateErr [ a; b ]

        static member (+) (a, b) = SysError.addError a b
        member a.add b = a + b
