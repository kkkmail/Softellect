namespace Softellect.DistributedProcessing.SolverRunner

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
open Softellect.DistributedProcessing.DataAccess.SolverRunner
//open Softellect.DistributedProcessing.WorkerNodeService.AppSettings
open Softellect.Sys.Primitives
open Softellect.Messaging.Client
open Softellect.Sys.Core

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

/// A SolverRunner operates on input data 'D and produces progress / output data 'P.
/// Internally it uses a data type 'T to store the state of the computation and may produce "chart" data 'C.
/// A "chart" is some transformation of 'T made at some steps. This is introduced because 'T can be huge and so
/// capturing the state on all steps is very memory consuming if possible at all. It is possible to sent 'T back
/// when needed and then let the partitioner deal with the transformations. But, again, 'T could be huge and it could be
/// in some very inconvenient "internal" form.
///
/// So, 4 generics are used: 'D, 'P, 'T, 'C and it is a minimum that's needed.
/// "Charts" are some slices of data that are produced during the computation and can be used to visualize the progress or for any other purposes.
/// We do it that way because both 'D and 'T could be huge and so capturing the state of the computation on each step is expensive.
/// Charts are relatively small and can be used to visualize the progress of the computation.
module Proxy =

    /// Everything that we need to know how to run the solver and report progress.
    type RunSolverData<'D, 'P, 'T, 'C> =
        {
            runQueueId : RunQueueId
            modelData : 'D
            noOfProgressPoints : int
            noOfChartPoints : int option // Charts are optional
            getProgressData : 'T -> ProgressData<'P>
            progressCallBack : RunQueueStatus option -> ProgressData<'P> -> unit
            chartDataUpdater : IAsyncUpdater<'T, 'C>
            checkCancellation : RunQueueId -> CancellationType option
            checkFreq : TimeSpan
        }


    /// TODO kk:20240908 - These seem to be the proxies that are used to communicate with the WorkerNodeService.
    type RunModelProxy<'D, 'P> =
        {
            sendRunModelMessage : DistributedProcessingMessageInfo<'D, 'P> -> MessagingUnitResult
            loadModelData : RunQueueId -> DistributedProcessingResult<'D>
            //controlData : RunnerControlData
        }


    type TryRunFirstModelProxy<'P> =
        {
            tryLoadFirstRunQueue : unit -> DistributedProcessingResult<RunQueue<'P> option>
            tryGetAvailableWorkerNode : unit -> DistributedProcessingResult<WorkerNodeId option>
            runModel : RunQueue<'P> -> DistributedProcessingUnitResult
            upsertRunQueue : RunQueue<'P> -> DistributedProcessingUnitResult
        }


    type TryCancelRunQueueProxy<'D, 'P> =
        {
            tryLoadRunQueue : RunQueueId -> DistributedProcessingResult<RunQueue<'P> option>
            sendCancelRunQueueMessage : DistributedProcessingMessageInfo<'D, 'P> -> MessagingUnitResult
            upsertRunQueue : RunQueue<'P> -> DistributedProcessingUnitResult
        }


    type TryRequestResultsProxy<'D, 'P> =
        {
            tryLoadRunQueue : RunQueueId -> DistributedProcessingResult<RunQueue<'P> option>
            sendRequestResultsMessage : DistributedProcessingMessageInfo<'D, 'P> -> MessagingUnitResult
        }


    type TryResetProxy =
        {
            tryResetRunQueue : RunQueueId -> DistributedProcessingUnitResult
        }


    type TryRunModelResult =
        | WorkScheduled
        | NoWork
        | NoAvailableWorkerNodes


    type TryRunAllModelsProxy =
        {
            tryRunFirstModel : unit -> DistributedProcessingResult<TryRunModelResult>
        }


    type UpdateProgressProxy<'D, 'P> =
        {
            tryLoadRunQueue : RunQueueId -> DistributedProcessingResult<RunQueue<'P> option>
            upsertRunQueue : RunQueue<'P> -> DistributedProcessingUnitResult
            upsertWorkerNodeErr : WorkerNodeId -> DistributedProcessingUnitResult
        }

        //static member create p : UpdateProgressProxy<'D, 'P> =
        //    {
        //        tryLoadRunQueue = tryLoadRunQueue<'D>
        //        upsertRunQueue = upsertRunQueue c
        //        upsertWorkerNodeErr = upsertWorkerNodeErr c p
        //    }


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
