namespace Softellect.Sys

open Softellect.Sys.GeneralErrors
open Softellect.Sys.TimerErrors
open Softellect.Sys.WcfErrors
open Softellect.Sys.DataAccessErrors
open Softellect.Sys.MessagingClientErrors
open Softellect.Sys.MessagingServiceErrors

module Errors =

    /// All errors known in the system.
    /// Use when it is convenient to have a unified error type and use underlying specific errors when it is not.
    type SoftellectError =
        | AggregateErr of SoftellectError * List<SoftellectError>
        | WcfErr of WcfError
        | TimerEventErr of TimerEventError
        | MessagingServiceErr of MessagingServiceError
        | MessagingClientErr of MessagingClientError
        | UnhandledExn of string * exn
        | ServiceInstallerErr of ServiceInstallerError
        //| RegistryErr of RegistryError
        //| FileErr of FileError
        | SerializationErr of SerializationError
        | DbErr of DbError

        static member addError a b =
            match a, b with
            | AggregateErr (x, w), AggregateErr (y, z) -> AggregateErr (x, w @ (y :: z))
            | AggregateErr (x, w), _ -> AggregateErr (x, w @ [b])
            | _, AggregateErr (y, z) -> AggregateErr (a, y :: z)
            | _ -> AggregateErr (a, [b])

        static member (+) (a, b) = SoftellectError.addError a b
        member a.add b = a + b
