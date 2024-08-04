namespace Softellect.DistributedProcessing

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
open Softellect.DistributedProcessing.DataAccess
open Softellect.DistributedProcessing.AppSettings
open Softellect.Sys.Primitives

module Proxy =

    type private UnitResult = DistributedProcessingUnitResult
    type private Message<'D, 'P> = Message<DistributedProcessingMessageData<'D, 'P>>
    type private MessageInfo<'D, 'P> = MessageInfo<DistributedProcessingMessageData<'D, 'P>>
    type private MessageProcessorProxy<'D, 'P> = MessageProcessorProxy<DistributedProcessingMessageData<'D, 'P>>


    type SendMessageProxy<'D, 'P> =
        {
            partitionerId : PartitionerId
            sendMessage : MessageInfo<'D, 'P> -> MessagingUnitResult
        }


    type OnUpdateProgressProxy<'D, 'P> =
        {
            tryDeleteWorkerNodeRunModelData : unit -> UnitResult
            tryUpdateProgressData : ProgressData<'P> -> UnitResult
            sendMessageProxy : SendMessageProxy<'D, 'P>
        }


    type OnProcessMessageProxy<'D> =
        {
            saveWorkerNodeRunModelData : WorkerNodeRunModelData<'D> -> UnitResult
            requestCancellation : RunQueueId -> CancellationType -> UnitResult
            notifyOfResults : RunQueueId -> ResultNotificationType -> UnitResult
            onRunModel : RunQueueId -> UnitResult
        }


    type WorkerNodeProxy<'D> =
        {
            onProcessMessageProxy : OnProcessMessageProxy<'D>
            loadAllActiveRunQueueId : unit -> DistributedProcessingResult<list<RunQueueId>>
            //logCrit : SolverRunnerCriticalError -> UnitResult
        }

        static member create c sr =
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
            messageProcessorProxy : MessageProcessorProxy<'D, 'P>
        }


    type OnRegisterProxy<'D, 'P> =
        {
            workerNodeInfo : WorkerNodeInfo
            sendMessageProxy : SendMessageProxy<'D, 'P>
        }


    type OnStartProxy =
        {
            loadAllActiveRunQueueId : unit -> DistributedProcessingResult<list<RunQueueId>>
            onRunModel : RunQueueId -> UnitResult
        }
