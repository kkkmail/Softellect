namespace Softellect.DistributedProcessing.WorkerNodeService

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
open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.WorkerNodeService.DataAccess
open Softellect.DistributedProcessing.WorkerNodeService.AppSettings
open Softellect.Sys.Primitives
open Softellect.Messaging.Client

module Proxy =

    type SendMessageProxy<'D, 'P> =
        {
            partitionerId : PartitionerId
            sendMessage : DistributedProcessingMessageInfo<'D, 'P> -> MessagingUnitResult
        }


    type OnUpdateProgressProxy<'D, 'P> =
        {
            tryDeleteWorkerNodeRunModelData : unit -> DistributedProcessingUnitResult
            tryUpdateProgressData : ProgressData<'P> -> DistributedProcessingUnitResult
            sendMessageProxy : SendMessageProxy<'D, 'P>
        }


    type OnProcessMessageProxy<'D> =
        {
            saveWorkerNodeRunModelData : WorkerNodeRunModelData<'D> -> DistributedProcessingUnitResult
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

        static member create c sr : WorkerNodeProxy<'D> =
            {
                onProcessMessageProxy =
                    {
                        saveWorkerNodeRunModelData = saveRunQueue c
                        requestCancellation = tryRequestCancelRunQueue c
                        notifyOfResults = fun q r -> tryNotifyRunQueue c q (Some r)
                        onRunModel = sr
                    }

                loadAllActiveRunQueueId = fun () -> loadAllActiveRunQueueId c
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
