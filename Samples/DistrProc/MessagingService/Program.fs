namespace Softellect.Samples.DistrProc.MessagingService

open Softellect.DistributedProcessing.MessagingService.Program

module Program =

    [<EntryPoint>]
    let main args = messagingServiceMain "MessagingService" args
