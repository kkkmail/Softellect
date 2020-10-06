namespace Softellect.Sys

open Softellect.Sys.GeneralErrors

module WcfErrors =

    type WcfError =
        | WcfServiceNotInitializedErr
        | WcfExn of exn
        | WcfSerializationErr of SerializationError
        | WcfCriticalErr of string
