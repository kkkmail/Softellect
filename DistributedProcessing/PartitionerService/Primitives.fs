namespace Softellect.DistributedProcessing.PartitionerService

open System
open Softellect.Sys.Primitives
open Softellect.DistributedProcessing.Primitives.Common

module Primitives =
    type RunQueue =
        {
            runQueueId : RunQueueId
            runQueueStatus : RunQueueStatus
            workerNodeIdOpt : WorkerNodeId option
            progressData : ProgressData
            createdOn : DateTime
        }
