namespace Softellect.Samples.Msg.ServiceInfo

open Softellect.Sys.Errors

module EchoMsgErrors =

    type EchoMsgError =
        | EchoMsgError


    type UnitResult = UnitResult<EchoMsgError>
    type EchoMsgResult<'T> = ResultWithErr<'T, EchoMsgError>
