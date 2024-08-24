namespace Softellect.Samples.Msg.WcfWorker

open Softellect.Messaging.Service
open Softellect.MessagingService.Program
open Softellect.Samples.Msg.ServiceInfo.Primitives
open Softellect.Samples.Msg.ServiceInfo.EchoMsgServiceInfo

module Program =

    [<EntryPoint>]
    let main args =
        let data =
            {
                messagingServiceProxy = serviceProxy
                messagingServiceAccessInfo = echMessagingServiceAccessInfo
            }

        main<EchoMessageData> "MsgWorker" data args
