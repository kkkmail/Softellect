namespace Softellect.DistributedProcessing

open System
open Softellect.Messaging.Primitives
open Softellect.Sys.Errors
open Softellect.Wcf.Common
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Client
open Softellect.Sys.Primitives

module Primitives =

    [<Literal>]
    let DefaultAbsoluteTolerance = 1.0e-08


    [<Literal>]
    let SolverRunnerName = "SolverRunner.exe"


    [<Literal>]
    let SolverRunnerProcessName = "SolverRunner"


    let defaultNoOfOutputPoints = 1000
    let defaultNoOfProgressPoints = 100


    let defaultServicePort = 5000


    let defaultPartitionerNetTcpServicePort = defaultServicePort |> ServicePort
    let defaultPartitionerHttpServicePort = defaultPartitionerNetTcpServicePort.value + 1 |> ServicePort
    let defaultPartitionerServiceAddress = localHost |> ServiceAddress


    type PartitionerId =
        | PartitionerId of MessagingClientId

        member this.value = let (PartitionerId v) = this in v.value
        member this.messagingClientId = let (PartitionerId v) = this in v


    let defaultPartitionerId = Guid("F941F87C-BEBC-43E7-ABD3-967E377CBD57") |> MessagingClientId |> PartitionerId


    type WorkerNodeConfigParam =
        | WorkerNumberOfSores of int


    /// An encapsulation of the evolution time in the system.
    /// It is convenient to have it as a separate type to avoid confusion with other decimal values.
    type EvolutionTime =
        | EvolutionTime of decimal

        member this.value = let (EvolutionTime v) = this in v
        static member defaultValue = EvolutionTime 0.0m


    /// A relative invariant is a value that should be close to 1.0 all the time.
    type RelativeInvariant =
        | RelativeInvariant of double

        member this.value = let (RelativeInvariant v) = this in v
        static member defaultValue = RelativeInvariant 1.0


    type AbsoluteTolerance =
        | AbsoluteTolerance of double

        member this.value = let (AbsoluteTolerance v) = this in v
        static member defaultValue = AbsoluteTolerance DefaultAbsoluteTolerance


    type ResultNotificationType =
        | RegularChartGeneration
        | ForceChartGeneration

        member n.value =
            match n with
            | RegularChartGeneration -> 1
            | ForceChartGeneration -> 2


    type CancellationType =
        | CancelWithResults of string option
        | AbortCalculation of string option

        member n.value =
            match n with
            | AbortCalculation _ -> 0
            | CancelWithResults _ -> 2


    let estimateEndTime progress (started : DateTime) =
        if progress > 0.0m && progress <= 1.0m
        then
            let estRunTime = (decimal (DateTime.Now.Subtract(started).Ticks)) / progress |> int64 |> TimeSpan.FromTicks
            started.Add estRunTime |> Some
        else None


    type ProgressData =
        {
            progress : decimal // Progress in the range [0.0, 1.0]
            callCount : int64
            t : EvolutionTime // Evolution time of the system. May coincide with callCount in some cases.
            relativeInvariant : RelativeInvariant // Should be close to 1.0 all the time. Substantial deviations is a sign of errors. If not needed, then set to 1.0.
            errorMessageOpt : ErrorMessage option
        }

        static member defaultValue : ProgressData =
            {
                progress = 0.0m
                callCount = 0L
                t = EvolutionTime.defaultValue
                relativeInvariant = RelativeInvariant.defaultValue
                errorMessageOpt = None
            }

        member data.estimateEndTime started = estimateEndTime data.progress started


    /// 'P is any other data that is needed for progress tracking.
    type ProgressData<'P> =
        {
            progressData : ProgressData
            progressDetailed : 'P option
        }

        static member defaultValue : ProgressData<'P> =
            {
                progressData = ProgressData.defaultValue
                progressDetailed = None
            }

        member data.estimateEndTime started = data.progressData.estimateEndTime started


    type WorkerNodeId =
        | WorkerNodeId of MessagingClientId

        member this.value = let (WorkerNodeId v) = this in v
        member this.messagingClientId = let (WorkerNodeId v) = this in v
        static member newId() = Guid.NewGuid() |> MessagingClientId |> WorkerNodeId


    type WorkerNodePriority =
        | WorkerNodePriority of int

        member this.value = let (WorkerNodePriority v) = this in v
        static member defaultValue = WorkerNodePriority 100


    type WorkerNodeName =
        | WorkerNodeName of string

        member this.value = let (WorkerNodeName v) = this in v
        static member newName() = $"{Guid.NewGuid()}".Replace("-", "") |> WorkerNodeName


    type SolverRunnerInfo =
        {
            runQueueId : RunQueueId
            processId : ProcessId option
        }


    ///// 'D is the model data.
    //type WorkerNodeRunModelData<'D> =
    //    {
    //        runQueueId : RunQueueId
    //        modelData : 'D
    //    }


    type WorkerNodeMessage<'D> =
        //| RunModelWrkMsg of WorkerNodeRunModelData<'D>
        | RunModelWrkMsg of (RunQueueId * 'D)
        | CancelRunWrkMsg of (RunQueueId * CancellationType)
        | RequestResultWrkMsg of (RunQueueId * ResultNotificationType)

        member this.messageSize =
            match this with
            | RunModelWrkMsg _ -> LargeSize
            | CancelRunWrkMsg _ -> SmallSize
            | RequestResultWrkMsg _ -> SmallSize


    /// Number of minutes for worker node errors to expire before the node can be again included in work distribution.
    type LastAllowedNodeErr =
        | LastAllowedNodeErr of int<minute>

        member this.value = let (LastAllowedNodeErr v) = this in v
        static member defaultValue = LastAllowedNodeErr 60<minute>


    //type WorkerNodeState =
    //    | NotStartedWorkerNode
    //    | StartedWorkerNode


    //type WorkerNodeRunnerState =
    //    {
    //        workerNodeState : WorkerNodeState
    //    }

    //    static member maxMessages = [ for _ in 1..maxNumberOfMessages -> () ]

    //    static member defaultValue =
    //        {
    //            workerNodeState = NotStartedWorkerNode
    //        }


    /// Information about a worker node to be passed to partitioner.
    type WorkerNodeInfo =
        {
            workerNodeId : WorkerNodeId
            workerNodeName : WorkerNodeName
            partitionerId : PartitionerId
            noOfCores : int
            nodePriority : WorkerNodePriority
            isInactive : bool
            lastErrorDateOpt : DateTime option
        }


    type ProgressUpdateInfo<'P> =
        {
            runQueueId : RunQueueId
            updatedRunQueueStatus : RunQueueStatus option
            progressData : ProgressData<'P>
        }


    type ChartInfo =
        {
            runQueueId : RunQueueId
            //defaultValueId : ClmDefaultValueId
            charts : list<HtmlChart>
        }


    type ChartGenerationResult =
        | GeneratedCharts of ChartInfo
        | NotGeneratedCharts


    /// Generic type parameter 'P is the type of additional progress data.
    /// It should account for intermediate progress data and final progress data.
    type PartitionerMessage<'P> =
        | UpdateProgressPrtMsg of ProgressUpdateInfo<'P>
        | SaveChartsPrtMsg of ChartInfo
        | RegisterWorkerNodePrtMsg of WorkerNodeInfo
        | UnregisterWorkerNodePrtMsg of WorkerNodeId

        member this.messageSize =
            match this with
            | UpdateProgressPrtMsg _ -> SmallSize
            | SaveChartsPrtMsg _ -> MediumSize
            | RegisterWorkerNodePrtMsg _ -> SmallSize
            | UnregisterWorkerNodePrtMsg _ -> SmallSize


    /// The decision was that we want strongly typed messages rather than untyped messages.
    /// Partitioner sends messages to WorkerNodes (WorkerNodeMessage<'D>) 
    /// and WorkerNodes send messages to Partitioner (PartitionerMessage<'P>).
    /// Single type could be used, but it seems inconvenient, as both partitioner and worker node would have to perform exhaustive pattern matching.
    type DistributedProcessingMessageData<'D, 'P> =
        | PartitionerMsg of PartitionerMessage<'P> // A message sent from worker node to partitioner.
        | WorkerNodeMsg of WorkerNodeMessage<'D> // A message sent from partitioner to worker node.

        static member maxInfoLength = 500

        member this.getMessageSize() =
            match this with
            | PartitionerMsg m -> m.messageSize
            | WorkerNodeMsg m -> m.messageSize


    type DistributedProcessingMessage<'D, 'P> = Message<DistributedProcessingMessageData<'D, 'P>>
    type DistributedProcessingMessageInfo<'D, 'P> = MessageInfo<DistributedProcessingMessageData<'D, 'P>>
    //type DistributedProcessingMessageProcessorProxy<'D, 'P> = MessageProcessorProxy<DistributedProcessingMessageData<'D, 'P>>


    type PartitionerMessageInfo<'P> =
        {
            partitionerRecipient : PartitionerId
            deliveryType : MessageDeliveryType
            messageData : PartitionerMessage<'P>
        }

        member this.getMessageInfo() =
            {
                recipientInfo =
                    {
                        recipient = this.partitionerRecipient.messagingClientId
                        deliveryType = this.deliveryType
                    }
                messageData = this.messageData |> PartitionerMsg |> UserMsg
            }


        //member q.modelCommandLineParam = q.info.modelCommandLineParam

        //static member fromModelCommandLineParam modelDataId defaultValueId p =
        //    {
        //        runQueueId = Guid.NewGuid() |> RunQueueId

        //        info =
        //            {
        //                modelDataId = modelDataId
        //                defaultValueId = defaultValueId
        //                modelCommandLineParam = p
        //            }

        //        runQueueStatus = NotStartedRunQueue
        //        workerNodeIdOpt = None
        //        progressData = ClmProgressData.defaultValue
        //        createdOn = DateTime.Now
        //    }

        //override r.ToString() =
        //    let (ModelDataId modelDataId) = r.info.modelDataId
        //    let (ClmDefaultValueId defaultValueId) = r.info.defaultValueId
        //    let (RunQueueId runQueueId) = r.runQueueId
        //    let s = (DateTime.Now - r.createdOn).ToString("d\.hh\:mm")

        //    let estCompl =
        //        match r.runQueueStatus, r.progressData.estimateEndTime r.createdOn with
        //        | InProgressRunQueue, Some e -> " ETC: " + e.ToString("yyyy-MM-dd.HH:mm") + ";"
        //        | _ -> EmptyString

        //    $"{{ T: %s{s};%s{estCompl} DF: %A{defaultValueId}; MDID: %A{modelDataId}; PID: %A{runQueueId}; %A{r.progressData.progressData.progress} }}"


    type CheckRunningResult =
        | CanRun
        | AlreadyRunning of ProcessId
        | TooManyRunning of int
        | GetProcessesByNameExn of exn


    type SolverType =
        | OdeSolver
        | FredholmSolver
        | UserDefinedSolver
