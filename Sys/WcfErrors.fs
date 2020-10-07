﻿namespace Softellect.Sys

open Softellect.Sys.GeneralErrors

module WcfErrors =

    type WcfError =
        | WcfServiceNotInitializedErr
        | WcfExn of exn
        | WcfServiceCannotInitializeErr of WcfError
        | WcfSerializationErr of SerializationError
        | WcfCriticalErr of string
