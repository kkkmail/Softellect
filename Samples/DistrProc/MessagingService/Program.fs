namespace Softellect.Samples.DistrProc.MessagingService

open Softellect.Messaging.Service
open Softellect.MessagingService.Program
open Softellect.Samples.DistrProc.ServiceInfo.Primitives
open Softellect.Samples.DistrProc.ServiceInfo.ServiceInfo
open Softellect.DistributedProcessing.Primitives.Common

module Program =

    [<EntryPoint>]
    let main args =
        let data =
            {
                messagingServiceProxy = serviceProxy
                messagingServiceAccessInfo = messagingServiceAccessInfo
            }

        main<DistributedProcessingMessageData> "MsgWorker" data args
