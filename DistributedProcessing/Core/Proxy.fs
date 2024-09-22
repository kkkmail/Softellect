namespace Softellect.DistributedProcessing.Proxy

open System
open System.Threading

open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Rop
open Softellect.Sys.TimerEvents

open Softellect.Wcf.Common
open Softellect.Wcf.Client

open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy
open System
open FSharp.Data.Sql
open Softellect.Sys.Core
open Softellect.Sys.Primitives
open Softellect.Sys.Retry
open Softellect.Sys.DataAccess
open Softellect.DistributedProcessing.VersionInfo
open Softellect.Sys.AppSettings
open Softellect.Messaging.Client
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.Primitives

#if WORKER_NODE
open Softellect.DistributedProcessing.WorkerNodeService.Primitives
open Softellect.DistributedProcessing.DataAccess.WorkerNodeService
#endif

// ==========================================

#if PARTITIONER
#endif

#if SOLVER_RUNNER || WORKER_NODE
#endif

#if SOLVER_RUNNER
#endif

#if WORKER_NODE
#endif

// ==========================================

#if PARTITIONER
module PartitionerService =
#endif

#if SOLVER_RUNNER
module SolverRunner =
#endif

#if WORKER_NODE
module WorkerNodeService =
#endif

// ==========================================

    // To make a compiler happy.
    let private dummy = 0

// ==========================================

#if PARTITIONER
#endif

#if SOLVER_RUNNER || WORKER_NODE
    type SendMessageProxy<'D, 'P> =
        {
            partitionerId : PartitionerId
            sendMessage : DistributedProcessingMessageInfo<'D, 'P> -> MessagingUnitResult
        }


    type OnUpdateProgressProxy<'D, 'P> =
        {
            // Was called tryDeleteWorkerNodeRunModelData.
            tryDeleteRunQueue : unit -> DistributedProcessingUnitResult
            tryUpdateProgressData : ProgressData<'P> -> DistributedProcessingUnitResult
            sendMessageProxy : SendMessageProxy<'D, 'P>
        }
#endif

#if SOLVER_RUNNER
#endif

#if WORKER_NODE
    type OnProcessMessageProxy<'D> =
        {
            saveModelData : RunQueueId -> 'D -> DistributedProcessingUnitResult
            requestCancellation : RunQueueId -> CancellationType -> DistributedProcessingUnitResult
            notifyOfResults : RunQueueId -> ResultNotificationType -> DistributedProcessingUnitResult
            onRunModel : RunQueueId -> DistributedProcessingUnitResult
        }


    type WorkerNodeProxy<'D> =
        {
            onProcessMessageProxy : OnProcessMessageProxy<'D>
            loadAllActiveRunQueueId : unit -> DistributedProcessingResult<list<RunQueueId>>
            //logCrit : SolverRunnerCriticalError -> UnitResult
        }

        static member create sr : WorkerNodeProxy<'D> =
            {
                onProcessMessageProxy =
                    {
                        saveModelData = saveModelData
                        requestCancellation = tryRequestCancelRunQueue
                        notifyOfResults = fun q r -> tryNotifyRunQueue q (Some r)
                        onRunModel = sr
                    }

                loadAllActiveRunQueueId = loadAllActiveRunQueueId
                //logCrit = saveSolverRunnerErrFs name
            }


    type WorkerNodeRunnerData<'D, 'P> =
        {
            workerNodeServiceInfo : WorkerNodeServiceInfo
            workerNodeProxy : WorkerNodeProxy<'D>
            //messageProcessorProxy : DistributedProcessingMessageProcessorProxy<'D, 'P>
            //messageProcessor : IMessageProcessor<DistributedProcessingMessageData<'D, 'P>>
            messagingClientData : MessagingClientData<DistributedProcessingMessageData<'D, 'P>>
            tryRunSolverProcess : int -> RunQueueId -> DistributedProcessingUnitResult
        }


    //type OnRegisterProxy<'D, 'P> =
    //    {
    //        workerNodeInfo : WorkerNodeInfo
    //        sendMessageProxy : SendMessageProxy<'D, 'P>
    //    }


    //type OnStartProxy =
    //    {
    //        loadAllActiveRunQueueId : unit -> DistributedProcessingResult<list<RunQueueId>>
    //        onRunModel : RunQueueId -> DistributedProcessingUnitResult
    //    }
#endif
