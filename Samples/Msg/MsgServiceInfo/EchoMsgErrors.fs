namespace Softellect.Samples.Msg.ServiceInfo

open Softellect.Sys.Errors
open Softellect.Sys.MessagingErrors

module EchoMsgErrors =

    type EchoMsgError =
        | EchoMsgErr of MessagingError


    type UnitResult = UnitResult<EchoMsgError>
    type EchoMsgResult<'T> = ResultWithErr<'T, EchoMsgError>
