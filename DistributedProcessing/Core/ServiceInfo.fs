namespace Softellect.DistributedProcessing

open Softellect.DistributedProcessing.Errors
open Microsoft.Extensions.Hosting

module ServiceInfo =

    type IWorkerNodeService =
        inherit IHostedService
        //abstract monitor : WorkerNodeMonitorParam -> ClmResult<WorkerNodeMonitorResponse>

        /// To check if service is working.
        abstract ping : unit -> DistributedProcessingUnitResult
