namespace Softellect.Apps.DistrProc.MessagingService

open Softellect.DistributedProcessing.MessagingService.Program

module Program =

    [<EntryPoint>]
    let main args = messagingServiceMain "DistributedProcessingMessagingService" args
