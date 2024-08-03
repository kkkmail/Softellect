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


    type SendMessageProxy<'D> =
        {
            partitionerId : PartitionerId
            sendMessage : MessageInfo<'D> -> MessagingUnitResult
        }


    type OnUpdateProgressProxy<'D, 'P> =
        {
            tryDeleteWorkerNodeRunModelData : unit -> UnitResult
            tryUpdateProgressData : ProgressData<'P> -> UnitResult
            sendMessageProxy : SendMessageProxy<'D>
        }


    type OnProcessMessageProxy<'D> =
        {
            saveWorkerNodeRunModelData : RunQueueId -> 'D -> UnitResult
            requestCancellation : RunQueueId -> CancellationType -> UnitResult
            notifyOfResults : RunQueueId -> ResultNotificationType -> UnitResult
            onRunModel : RunQueueId -> UnitResult
        }


    type WorkerNodeProxy<'D> =
        {
            onProcessMessageProxy : OnProcessMessageProxy<'D>
            loadAllActiveRunQueueId : unit -> DistributedProcessingResult<list<RunQueueId>>
            logCrit : SolverRunnerCriticalError -> UnitResult
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
                logCrit = saveSolverRunnerErrFs name
            }


    type OnRegisterProxy<'D> =
        {
            workerNodeInfo : WorkerNodeInfo
            sendMessageProxy : SendMessageProxy<'D>
        }


    type OnStartProxy =
        {
            loadAllActiveRunQueueId : unit -> DistributedProcessingResult<list<RunQueueId>>
            onRunModel : RunQueueId -> UnitResult
        }
