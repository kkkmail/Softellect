namespace Softellect.Samples.Msg.ServiceInfo

open Softellect.Sys.Rop
open Softellect.Sys.MessagingErrors

module EchoMsgErrors =

    type EchoMsgError =
        | EchoMsgErr of MessagingError
        | AggregateErr of EchoMsgError * List<EchoMsgError>

        static member addError a b =
            match a, b with
            | AggregateErr (x, w), AggregateErr (y, z) -> AggregateErr (x, w @ (y :: z))
            | AggregateErr (x, w), _ -> AggregateErr (x, w @ [b])
            | _, AggregateErr (y, z) -> AggregateErr (a, y :: z)
            | _ -> AggregateErr (a, [b])


    type UnitResult = UnitResult<EchoMsgError>
    type EchoMsgResult<'T> = Result<'T, EchoMsgError>
