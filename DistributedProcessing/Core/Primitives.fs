namespace Softellect.DistributedProcessing

open System
open System.Threading
open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Rop
open Softellect.Sys.Errors
open Softellect.Sys.TimerEvents
open Softellect.Wcf.Common
open Softellect.Wcf.Client
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy
open Softellect.Messaging.Client
open Softellect.Sys.Primitives

module Primitives =

    [<Literal>]
    let DefaultAbsoluteTolerance = 1.0e-08

    let defaultNoOfOutputPoints = 1000
    let defaultNoOfProgressPoints = 100


    let workerNodeServiceProgramName = "WorkerNodeService.exe"


    [<Literal>]
    let WorkerNodeWcfServiceName = "WorkerNodeWcfService"

    let defaultServicePort = 5000 // + messagingDataVersion.value


    let defaultContGenNetTcpServicePort = defaultServicePort |> ServicePort
    let defaultContGenHttpServicePort = defaultContGenNetTcpServicePort.value + 1 |> ServicePort
    let defaultContGenServiceAddress = localHost |> ServiceAddress


    let defaultWorkerNodeNetTcpServicePort = 20000 + defaultServicePort |> ServicePort
    let defaultWorkerNodeHttpServicePort = defaultWorkerNodeNetTcpServicePort.value + 1 |> ServicePort
    let defaultWorkerNodeServiceAddress = localHost |> ServiceAddress


    type PartitionerId =
        | PartitionerId of MessagingClientId
        //| PartitionerId of Guid

        member this.value = let (PartitionerId v) = this in v.value
        member this.messagingClientId = let (PartitionerId v) = this in v


    let defaultPartitionerId = Guid("F941F87C-BEBC-43E7-ABD3-967E377CBD57") |> MessagingClientId |> PartitionerId


    type WorkerNodeConfigParam =
        | WorkerNumberOfSores of int


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


    let private estimateEndTime progress (started : DateTime) =
        if progress > 0.0m && progress <= 1.0m
        then
            let estRunTime = (decimal (DateTime.Now.Subtract(started).Ticks)) / progress |> int64 |> TimeSpan.FromTicks
            started.Add estRunTime |> Some
        else None


    type ProgressData<'P> =
        {
            progress : decimal
            callCount : int64
            relativeInvariant : double // Should be close to 1.0 all the time. Substantial deviations is a sign of errors. If not needed, then set to 1.0.
            errorMessageOpt : ErrorMessage option
            progressData : 'P
        }

        static member getDefaultValue p =
            {
                progress = 0.0m
                callCount = 0L
                relativeInvariant = 1.0
                errorMessageOpt = None
                progressData = p
            }

        member data.estimateEndTime (started : DateTime) = estimateEndTime data.progress started


    type WorkerNodeServiceName =
        | WorkerNodeServiceName of ServiceName

        member this.value = let (WorkerNodeServiceName v) = this in v
        static member netTcpServiceName = "WorkerNodeNetTcpService" |> ServiceName |> WorkerNodeServiceName
        static member httpServiceName = "WorkerNodeHttpService" |> ServiceName |> WorkerNodeServiceName


    type WorkerNodeId =
        | WorkerNodeId of MessagingClientId

        member this.value = let (WorkerNodeId v) = this in v
        member this.messagingClientId = let (WorkerNodeId v) = this in v


    type WorkerNodePriority =
        | WorkerNodePriority of int

        member this.value = let (WorkerNodePriority v) = this in v
        static member defaultValue = WorkerNodePriority 100


    type WorkerNodeName =
        | WorkerNodeName of string

        member this.value = let (WorkerNodeName v) = this in v


    type SolverRunnerInfo =
        {
            runQueueId : RunQueueId
            processId : ProcessId option
        }


    ///// 'D is the model data and 'C is the control data.
    //type WorkerNodeRunModelData<'D, 'C> =
    //    {
    //        runningProcessData : RunningProcessData
    //        modelData : 'D
    //        controlData : 'C
    //    }
    type WorkerNodeRunModelData<'D> =
        {
            runQueueId : RunQueueId
            modelData : 'D
        }


    type WorkerNodeMessage<'D> =
        | RunModelWrkMsg of WorkerNodeRunModelData<'D>
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


    type WorkerNodeState =
        | NotStartedWorkerNode
        | StartedWorkerNode


    type WorkerNodeRunnerState =
        {
            workerNodeState : WorkerNodeState
        }

        static member maxMessages = [ for _ in 1..maxNumberOfMessages -> () ]

        static member defaultValue =
            {
                workerNodeState = NotStartedWorkerNode
            }


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


    type WorkerNodeServiceAccessInfo =
        | WorkerNodeServiceAccessInfo of ServiceAccessInfo

        member w.value = let (WorkerNodeServiceAccessInfo v) = w in v

        //static member create address httpPort netTcpPort securityMode =
        //    let h = HttpServiceAccessInfo.create address httpPort WorkerNodeServiceName.httpServiceName.value
        //    let n = NetTcpServiceAccessInfo.create address netTcpPort WorkerNodeServiceName.netTcpServiceName.value securityMode
        //    ServiceAccessInfo.create h n |> WorkerNodeServiceAccessInfo

        static member defaultServiceAccessInfo v =
            {
                netTcpServiceAddress = ServiceAddress localHost
                netTcpServicePort = defaultContGenNetTcpServicePort
                netTcpServiceName = WorkerNodeWcfServiceName |> ServiceName
                netTcpSecurityMode = NoSecurity
            }
            |> NetTcpServiceInfo



    type WorkerNodeServiceInfo =
        {
            workerNodeInfo : WorkerNodeInfo
            workerNodeServiceAccessInfo : WorkerNodeServiceAccessInfo
            messagingServiceAccessInfo : MessagingServiceAccessInfo
        }

        member this.messagingClientAccessInfo =
            {
                msgClientId = this.workerNodeInfo.workerNodeId.messagingClientId
                msgSvcAccessInfo = this.messagingServiceAccessInfo
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


    /// Generic type parameter 'P is the type of progress data.
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


    type WorkerNodeMessageInfo<'D> =
        {
            workerNodeRecipient : WorkerNodeId
            deliveryType : MessageDeliveryType
            messageData : WorkerNodeMessage<'D>
        }

        member this.getMessageInfo() =
            {
                recipientInfo =
                    {
                        recipient = this.workerNodeRecipient.messagingClientId
                        deliveryType = this.deliveryType
                    }
                messageData = this.messageData |> WorkerNodeMsg |> UserMsg
            }


    type RunQueue<'P> =
        {
            runQueueId : RunQueueId
            //info : RunQueueInfo
            runQueueStatus : RunQueueStatus
            workerNodeIdOpt : WorkerNodeId option
            progressData : ProgressData<'P>
            createdOn : DateTime
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
