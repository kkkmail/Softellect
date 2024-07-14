namespace Softellect.Samples.Msg.WcfWorker

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.MessagingService
open Softellect.MessagingService.Program
open Softellect.Samples.Msg.ServiceInfo.Primitives

module Program =

    [<EntryPoint>]
    let main args = main<EchoMessageData> "MsgWorker" echoDataVersion args
