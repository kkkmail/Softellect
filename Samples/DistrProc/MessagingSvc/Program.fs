namespace Softellect.Samples.Msg.WcfWorker

open Softellect.Messaging.Service
open Softellect.MessagingService.Program
open Softellect.Samples.DistrProc.ServiceInfo.Primitives
open Softellect.Samples.DistrProc.ServiceInfo.ServiceInfo

module Program =

    [<EntryPoint>]
    let main args =
        let data =
            {
                messagingServiceProxy = serviceProxy
                messagingServiceAccessInfo = messagingServiceAccessInfo
            }

        main<TestMessageData> "MsgWorker" data args
