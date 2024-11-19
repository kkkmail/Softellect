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
        | GeneralFileExn of exn
        | GetFolderNameExn of exn
        | GetFileNameExn of exn
        | FileNotFoundErr of string
        | ReadFileExn of exn
        | WriteFileExn of exn
        | DeleteFileExn of exn
        | GetObjectIdsExn of exn
        | TryEnsureFolderExistsExn of exn
        | TryGetFullFileNameExn of exn
        | TryGetFolderNameExn of exn
        | TryGetExtensionExn of exn
        | TryOpenJsonExn of exn


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


    type SysError =
        | JsonParseErr of JsonParseError
        | SerializationErr of SerializationError
        | ServiceInstallerErr of ServiceInstallerError
        | DbErr of DbError
        | FileErr of FileError
        | TimerEventErr of TimerEventError
        | CryptoErr of CryptoError
