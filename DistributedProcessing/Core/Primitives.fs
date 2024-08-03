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
open Softellect.Sys.Primitives

module Primitives =

    [<Literal>]
    let DefaultAbsoluteTolerance = 1.0e-08

    let defaultNoOfOutputPoints = 1000
    let defaultNoOfProgressPoints = 100


    let workerNodeServiceProgramName = "WorkerNodeService.exe"


    [<Literal>]
    let WorkerNodeWcfServiceName = "WorkerNodeWcfService"

    let defaultServicePort : int = 5000 // + messagingDataVersion.value


    let defaultContGenNetTcpServicePort = defaultServicePort |> ServicePort
    let defaultContGenHttpServicePort = defaultContGenNetTcpServicePort.value + 1 |> ServicePort
    let defaultContGenServiceAddress = LocalHost |> ServiceAddress


    let defaultWorkerNodeNetTcpServicePort = 20000 + defaultServicePort |> ServicePort
    let defaultWorkerNodeHttpServicePort = defaultWorkerNodeNetTcpServicePort.value + 1 |> ServicePort
    let defaultWorkerNodeServiceAddress = LocalHost |> ServiceAddress


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


    type WorkerNodeMessage<'D> =
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


    type WorkerNodeState =
        | NotStartedWorkerNode
        | StartedWorkerNode


    type WorkerNodeRunnerState =
        {
            workerNodeState : WorkerNodeState
        }
