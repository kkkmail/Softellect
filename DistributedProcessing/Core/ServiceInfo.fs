namespace Softellect.DistributedProcessing

open Softellect.DistributedProcessing.Errors

module ServiceInfo =

    type IWorkerNodeService =
        //abstract monitor : WorkerNodeMonitorParam -> ClmResult<WorkerNodeMonitorResponse>

        /// To check if service is working.
        abstract ping : unit -> DistributedProcessingUnitResult
