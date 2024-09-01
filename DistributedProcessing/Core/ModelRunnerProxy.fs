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
open Softellect.Messaging.Client

//open ClmSys.ContGenData
//open Primitives.GeneralPrimitives
//open Softellect.Messaging.ServiceInfo
//open ClmSys.ClmErrors
//open ClmSys.ContGenPrimitives
//open ClmSys.GeneralPrimitives
//open ClmSys.WorkerNodeData
//open ClmSys.SolverData
//open ClmSys.WorkerNodePrimitives
//open ClmSys.Logging
//open Clm.ModelParams
//open Clm.CalculationData
//open ContGenServiceInfo.ServiceInfo
//open MessagingServiceInfo.ServiceInfo
//open DbData.DatabaseTypesDbo
//open DbData.DatabaseTypesClm
//open ServiceProxy.MsgProcessorProxy
//open NoSql.FileSystemTypes
//open Softellect.Sys.Primitives

module ModelRunnerProxy =
    let x = 1

    //type RunModelProxy<'D, 'P> =
    //    {
    //        sendRunModelMessage : DistributedProcessingMessageInfo<'D, 'P> -> MessagingUnitResult
    //        loadModelData : RunQueueId -> DistributedProcessingResult<'D>
    //        controlData : RunnerControlData
    //    }


    //type TryRunFirstModelProxy<'P> =
    //    {
    //        tryLoadFirstRunQueue : unit -> DistributedProcessingResult<RunQueue<'P> option>
    //        tryGetAvailableWorkerNode : unit -> DistributedProcessingResult<WorkerNodeId option>
    //        runModel : RunQueue<'P> -> DistributedProcessingUnitResult
    //        upsertRunQueue : RunQueue<'P> -> DistributedProcessingUnitResult
    //    }


    //type TryCancelRunQueueProxy<'D, 'P> =
    //    {
    //        tryLoadRunQueue : RunQueueId -> DistributedProcessingResult<RunQueue<'P> option>
    //        sendCancelRunQueueMessage : DistributedProcessingMessageInfo<'D, 'P> -> MessagingUnitResult
    //        upsertRunQueue : RunQueue<'P> -> DistributedProcessingUnitResult
    //    }


    //type TryRequestResultsProxy<'D, 'P> =
    //    {
    //        tryLoadRunQueue : RunQueueId -> DistributedProcessingResult<RunQueue<'P> option>
    //        sendRequestResultsMessage : DistributedProcessingMessageInfo<'D, 'P> -> MessagingUnitResult
    //    }


    //type TryResetProxy =
    //    {
    //        tryResetRunQueue : RunQueueId -> DistributedProcessingUnitResult
    //    }


    //type TryRunModelResult =
    //    | WorkScheduled
    //    | NoWork
    //    | NoAvailableWorkerNodes


    //type TryRunAllModelsProxy =
    //    {
    //        tryRunFirstModel : unit -> DistributedProcessingResult<TryRunModelResult>
    //    }


    //type UpdateProgressProxy<'P> =
    //    {
    //        tryLoadRunQueue : RunQueueId -> DistributedProcessingResult<RunQueue<'P> option>
    //        upsertRunQueue : RunQueue<'P> -> DistributedProcessingUnitResult
    //        upsertWorkerNodeErr : WorkerNodeId -> DistributedProcessingUnitResult
    //    }

    //    static member create c p =
    //        {
    //            tryLoadRunQueue = tryLoadRunQueue c
    //            upsertRunQueue = upsertRunQueue c
    //            upsertWorkerNodeErr = upsertWorkerNodeErr c p
    //        }


    //type RegisterProxy =
    //    {
    //        upsertWorkerNodeInfo : WorkerNodeInfo -> DistributedProcessingUnitResult
    //    }

    //    static member create c =
    //        {
    //            upsertWorkerNodeInfo = upsertWorkerNodeInfo c
    //        }


    //type UnregisterProxy =
    //    {
    //        loadWorkerNodeInfo : WorkerNodeId -> DistributedProcessingResult<WorkerNodeInfo>
    //        upsertWorkerNodeInfo : WorkerNodeInfo -> DistributedProcessingUnitResult
    //    }
    //    static member create c p =
    //        {
    //            loadWorkerNodeInfo = loadWorkerNodeInfo c p
    //            upsertWorkerNodeInfo = upsertWorkerNodeInfo c
    //        }


    //type SaveChartsProxy =
    //    {
    //        saveCharts : ChartInfo -> DistributedProcessingUnitResult
    //    }

    //    static member create resultLocation =
    //        {
    //            saveCharts = fun (c : ChartInfo) -> saveLocalChartInfo (Some (resultLocation, c.defaultValueId)) c
    //        }


    //type ProcessMessageProxy<'P> =
    //    {
    //        updateProgress : ProgressUpdateInfo<'P> -> DistributedProcessingUnitResult
    //        saveCharts : ChartInfo -> DistributedProcessingUnitResult
    //        register : WorkerNodeInfo -> DistributedProcessingUnitResult
    //        unregister : WorkerNodeId -> DistributedProcessingUnitResult
    //    }


    //type GetRunStateProxy<'P> =
    //    {
    //        loadRunQueueProgress : unit -> ListResult<RunQueue<'P>, DistributedProcessingError>
    //    }

    //    static member create c =
    //        {
    //            loadRunQueueProgress = fun () -> loadRunQueueProgress c
    //        }


    //type RunnerProxy =
    //    {
    //        getMessageProcessorProxy : MessagingClientAccessInfo -> MessageProcessorProxy
    //    }


    //type RunnerData =
    //    {
    //        getConnectionString : unit -> ConnectionString
    //        contGenInfo : ContGenInfo
    //    }


    //type RunModelProxy
    //    with
    //    static member create (d : RunnerData) s =
    //        {
    //            sendRunModelMessage = s
    //            loadModelData = loadModelData d.getConnectionString
    //            controlData = d.contGenInfo.controlData
    //        }


    //type RunnerDataWithProxy =
    //    {
    //        runnerData : RunnerData
    //        messageProcessorProxy : MessageProcessorProxy
    //    }


    //type ModelRunnerDataWithProxy =
    //    {
    //        runnerData : RunnerData
    //        runnerProxy : RunnerProxy
    //        messagingClientAccessInfo : MessagingClientAccessInfo
    //        logger : Logger
    //    }
