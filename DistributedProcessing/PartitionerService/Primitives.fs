namespace Softellect.DistributedProcessing.PartitionerService

open System
open Softellect.Sys.Primitives
open Softellect.DistributedProcessing.Primitives.Common

module Primitives =

    let partitionerServiceProgramName = "PartitionerService.exe"


    type RunQueue =
        {
            runQueueId : RunQueueId
            runQueueStatus : RunQueueStatus
            solverId : SolverId
            workerNodeIdOpt : WorkerNodeId option
            progressData : ProgressData
            createdOn : DateTime
        }
