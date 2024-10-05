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

        messagingMain<DistributedProcessingMessageData> "MsgWorker" data args
