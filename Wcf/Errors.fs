namespace Softellect.Wcf

open Softellect.Sys.Errors

module Errors =

    type WcfError =
        | WcfServiceNotInitializedErr
        | WcfExn of exn
        | WcfServiceCannotInitializeErr
        | WcfSerializationErr of SerializationError
        | WcfCriticalErr of string
