namespace Softellect.Samples.Msg.WcfWorker

open Softellect.MessagingService.Program
open Softellect.Samples.Msg.ServiceInfo.Primitives

module Program =

    [<EntryPoint>]
    let main args = main<EchoMessageData> "MsgWorker" echoDataVersion args
