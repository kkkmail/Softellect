﻿namespace Softellect.Samples.Messaging.MessagingServiceInfo

open Softellect.Sys.Errors

module EchoMsgErrors =

    type EchoMsgError =
        | EchoMsgError


    type UnitResult = UnitResult<EchoMsgError>
    type EchoMsgResult<'T> = StlResult<'T, EchoMsgError>
