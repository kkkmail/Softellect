namespace Softellect.DistributedProcessing.PartitionerService

////open Primitives.GeneralPrimitives
////open Primitives.SolverPrimitives
//open Softellect.Sys.Rop
//open Softellect.Messaging.Primitives
//open Softellect.Messaging.Client
//open Softellect.Messaging.Proxy

////open Clm.ModelParams
//open Softellect.Messaging.ServiceInfo
////open ContGenServiceInfo.ServiceInfo
////open ClmSys.ClmErrors
////open ClmSys.ContGenPrimitives
////open ClmSys.TimerEvents
////open ClmSys.PartitionerData
////open ServiceProxy.ModelRunnerProxy
////open ClmSys.ModelRunnerErrors
////open MessagingServiceInfo.ServiceInfo
////open ClmSys.PartitionerPrimitives
////open ClmSys.Logging
////open DbData.DatabaseTypesDbo
////open DbData.DatabaseTypesClm
////open ServiceProxy.MsgProcessorProxy
//open Softellect.Messaging.Errors
//open Primitives
////open ModelGenerator

open System
open Argu
open System.Threading
open System.Threading.Tasks
open System.ServiceModel
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open Softellect.Sys
open Softellect.Sys.Primitives
open Softellect.Messaging.Primitives
open Softellect.Sys.AppSettings
open Softellect.Wcf.Common
open Softellect.Wcf.Client
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.AppSettings
open Softellect.Sys.AppSettings
open Softellect.Wcf.AppSettings

open Softellect.Sys.Primitives
open Softellect.Messaging.Primitives
open Softellect.Messaging.AppSettings
open Softellect.Messaging.Errors
open Softellect.Sys.AppSettings
open Softellect.Wcf.Common

open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.AppSettings.PartitionerService
open Softellect.DistributedProcessing.PartitionerService.Primitives
open Softellect.DistributedProcessing.Errors
//open Softellect.DistributedProcessing.PartitionerService.
open Softellect.Sys.AppSettings
open Softellect.Sys
open Softellect.Wcf.Common
open Softellect.Wcf.Service
open CoreWCF
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.Proxy.PartitionerService
open Softellect.Messaging.Client
open Softellect.Messaging.Proxy
open Softellect.Sys.TimerEvents
open Softellect.Sys.Rop

module Partitioner =

    let private printDebug s = printfn $"{s}"
//    let private printDebug s = ()

    let private toError g f = f |> g |> Error
    let private addError g f e = ((f |> g) + e) |> Error
    let private combineUnitResults = combineUnitResults DistributedProcessingError.addError
    //let private maxMessages = [ for _ in 1..maxNumberOfMessages -> () ]

    //type OnProcessMessageType = OnProcessMessageType<unit>
    //type OnGetMessagesProxy = OnGetMessagesProxy<unit>
    //let onGetMessages = onGetMessages<unit>


    let private toMessageInfoOpt getModelData w (q : RunQueue) =
        match getModelData q.runQueueId with
        | Ok m ->
            {
                workerNodeRecipient = w
                deliveryType = GuaranteedDelivery
                messageData = (q.runQueueId, m) |> RunModelWrkMsg
            }.getMessageInfo()
            |> Some |> Ok
        | Error e -> Error e


    let runModel (proxy : PartitionerProxy) w (q : RunQueue) : DistributedProcessingUnitResult =
        match toMessageInfoOpt proxy.loadModelData w q with
        | Ok (Some m) ->
            match proxy.sendRunModelMessage m with
            | Ok v -> Ok v
            | Error e -> addError RunModelRunnerErr MessagingRunnerErr e
        | Ok None -> q.runQueueId |> MissingWorkerNodeRunnerErr |> toError RunModelRunnerErr
        | Error e -> (addError RunModelRunnerErr) (UnableToLoadModelDataRunnerErr q.runQueueId) e


    /// Tries to run the first not scheduled run queue entry using the first available worker node.
    let tryRunFirstModel (proxy : PartitionerProxy) =
        let addError = addError TryRunFirstModelRunnerErr

        match proxy.tryLoadFirstRunQueue() with
        | Ok (Some q) ->
            match proxy.tryGetAvailableWorkerNode() with
            | Ok (Some n) ->
                let q1 = { q with workerNodeIdOpt = Some n; runQueueStatus = RunRequestedRunQueue }

                match proxy.upsertRunQueue q1 with
                | Ok() ->
                    match proxy.runModel q1 with
                    | Ok() -> Ok WorkScheduled
                    | Error e ->
                        match proxy.upsertRunQueue { q1 with runQueueStatus = FailedRunQueue } with
                        | Ok() -> addError UnableToRunModelRunnerErr e
                        | Error f -> addError UnableToRunModelAndUpsertStatusRunnerErr (f + e)
                | Error e -> addError UpsertRunQueueRunnerErr e
            | Ok None -> Ok NoAvailableWorkerNodes
            | Error e -> addError UnableToGetWorkerNodeRunnerErr e
        | Ok None -> Ok NoWork
        | Error e -> addError TryLoadFirstRunQueueRunnerErr e


    let tryCancelRunQueue (proxy : PartitionerProxy) (q, c) =
        let addError = addError TryCancelRunQueueRunnerErr
        let toError = toError TryCancelRunQueueRunnerErr

        match proxy.tryLoadRunQueue q with
        | Ok (Some r) ->
            let r1 =
                match r.workerNodeIdOpt with
                | Some w ->
                    let r11 =
                        {
                            recipientInfo =
                                {
                                    recipient = w.messagingClientId
                                    deliveryType = GuaranteedDelivery
                                }

                            messageData = (q, c) |> CancelRunWrkMsg |> WorkerNodeMsg |> UserMsg
                        }
                        |> proxy.sendCancelRunQueueMessage

                    match r11 with
                    | Ok v -> Ok v
                    | Error e -> TryCancelRunQueueRunnerError.MessagingTryCancelRunQueueRunnerErr e |> toError
                | None -> Ok()

            let r2 =
                match r.runQueueStatus with
                | NotStartedRunQueue -> { r with runQueueStatus = CancelledRunQueue } |> proxy.upsertRunQueue
                | RunRequestedRunQueue -> { r with runQueueStatus = CancelRequestedRunQueue } |> proxy.upsertRunQueue
                | InProgressRunQueue -> { r with runQueueStatus = CancelRequestedRunQueue } |> proxy.upsertRunQueue
                | CancelRequestedRunQueue -> { r with runQueueStatus = CancelRequestedRunQueue } |> proxy.upsertRunQueue
                | _ -> q |> TryCancelRunQueueRunnerError.InvalidRunQueueStatusRunnerErr |> toError

            combineUnitResults r1 r2
        | Ok None -> toError (TryCancelRunQueueRunnerError.TryLoadRunQueueRunnerErr q)
        | Error e -> addError (TryCancelRunQueueRunnerError.TryLoadRunQueueRunnerErr q) e


    let tryRequestResults (proxy : PartitionerProxy) (q, c) =
        let addError = addError TryRequestResultsRunnerErr
        let toError = toError TryRequestResultsRunnerErr

        match proxy.tryLoadRunQueue q with
        | Ok (Some r) ->
            match r.workerNodeIdOpt with
            | Some w ->
                let r1 =
                    {
                        recipientInfo =
                            {
                                recipient = w.messagingClientId
                                deliveryType = GuaranteedDelivery
                            }

                        messageData = (q, c) |> RequestChartsWrkMsg |> WorkerNodeMsg |> UserMsg
                    }
                    |> proxy.sendRequestResultsMessage

                match r1 with
                | Ok v -> Ok v
                | Error e -> MessagingTryRequestResultsRunnerErr e |> toError
            | None -> Ok()
        | Ok None -> toError (TryRequestResultsRunnerError.TryLoadRunQueueRunnerErr q)
        | Error e -> addError (TryRequestResultsRunnerError.TryLoadRunQueueRunnerErr q) e


    let tryReset (proxy : PartitionerProxy) q =
        proxy.tryResetRunQueue q


    /// Tries to run all available work items (run queue) on all available work nodes until one or the other is exhausted.
    let tryRunAllModels (proxy : PartitionerProxy) =
        let rec doWork() =
            match proxy.tryRunFirstModel() with
            | Ok r ->
                match r with
                | WorkScheduled -> doWork()
                | NoWork -> Ok()
                | NoAvailableWorkerNodes -> Ok()
            | Error e -> addError TryRunAllModelsRunnerErr UnableToTryRunFirstModelRunnerErr e

        doWork()


    let updateProgress (proxy : PartitionerProxy) (i : ProgressUpdateInfo) =
        printDebug $"updateProgress: i = %A{i}"
        let addError = addError UpdateProgressRunnerErr
        let toError = toError UpdateProgressRunnerErr

        match proxy.tryLoadRunQueue i.runQueueId with
        | Ok (Some q) ->
            let q1 = { q with progressData = i.progressData }

            let upsert q2 =
                printfn $"updateProgress.upsert: Upserting %A{i} into %A{q2}."

                match proxy.upsertRunQueue q2 with
                | Ok() -> Ok()
                | Error e -> addError (UnableToLoadRunQueueRunnerErr i.runQueueId) e
                // (r, addError (UnableToLoadRunQueueErr i.runQueueId) e) ||> combineUnitResults

            match i.updatedRunQueueStatus with
            | Some s -> { q1 with runQueueStatus = s }
            | None -> q1
            |> upsert
        | Ok None -> toError (UnableToFindLoadRunQueueRunnerErr i.runQueueId)
        | Error e -> addError (UnableToLoadRunQueueRunnerErr i.runQueueId) e


    let register (proxy : PartitionerProxy) (r : WorkerNodeInfo) =
        //printfn "register: r = %A" r
        proxy.upsertWorkerNodeInfo r |> bindError (addError RegisterRunnerErr (UnableToUpsertWorkerNodeInfoRunnerErr r.workerNodeId))


    let unregister (proxy : PartitionerProxy) (r : WorkerNodeId) =
        //printfn "unregister: r = %A" r
        let addError = addError UnregisterRunnerErr

        match proxy.loadWorkerNodeInfo r with
        | Ok w -> proxy.upsertWorkerNodeInfo { w with noOfCores = 0 } |> bindError (addError (UnableToUpsertWorkerNodeInfoOnUnregisterRunnerErr r))
        | Error e -> addError (UnableToLoadWorkerNodeInfoRunnerErr r) e


    let saveCharts (proxy : PartitionerProxy) (c : ChartInfo) =
        printfn $"saveCharts: c.runQueueId = %A{c.runQueueId}"
        proxy.saveCharts c |> bindError (addError SaveChartsRunnerErr (UnableToSaveChartsRunnerErr c.runQueueId))


//    let processMessage (proxy : ProcessMessageProxy) (m : Message) =
//        printfn $"processMessage: messageId = %A{m.messageDataInfo.messageId}, message = %A{m}."

//        let r =
//            match m.messageData with
//            | UserMsg (PartitionerMsg x) ->
//                match x with
//                | UpdateProgressPrtMsg i -> proxy.updateProgress i
//                | SaveChartsPrtMsg c -> proxy.saveCharts c
//                | RegisterWorkerNodePrtMsg r -> proxy.register r
//                | UnregisterWorkerNodePrtMsg r -> proxy.unregister r
//                |> bindError (addError ProcessMessageRunnerErr (ErrorWhenProcessingMessageRunnerErr m.messageDataInfo.messageId))
//            | _ -> toError ProcessMessageRunnerErr (InvalidMessageTypeRunnerErr m.messageDataInfo.messageId)

//        match r with
//        | Ok() -> ()
//        | Error e -> printfn $"processMessage: messageId = %A{m.messageDataInfo.messageId}, ERROR = %A{e}."

//        r


//    let getRunState (proxy : GetRunStateProxy) =
//        let w, e = proxy.loadRunQueueProgress() |> unzipListResult
//        w, e |> foldToUnitResult

// ====================================

    type IPartitionerService =
        inherit IHostedService

        abstract tryCancelRunQueue : RunQueueId -> CancellationType -> DistributedProcessingUnitResult
        abstract tryRequestResults : RunQueueId -> DistributedProcessingUnitResult -> DistributedProcessingUnitResult
        abstract tryReset : RunQueueId -> DistributedProcessingUnitResult


    /// https://gist.github.com/dgfitch/661656
    [<ServiceContract(ConfigurationName = PartitionerWcfServiceName)>]
    type IPartitionerWcfService =

        [<OperationContract(Name = "tryCancelRunQueue")>]
        abstract tryCancelRunQueue : q:byte[] -> byte[]

        [<OperationContract(Name = "tryRequestResults")>]
        abstract tryRequestResults : q:byte[] -> byte[]

        [<OperationContract(Name = "tryReset")>]
        abstract tryReset : q:byte[] -> byte[]


//    type PartitionerWcfService(w : IPartitionerService) =

//        let toPingError f = f |> PingWcfErr |> PartitionerWcfErr

//        let ping() = w.ping()

//        interface IPartitionerWcfService with
//            member _.ping b = tryReply ping toPingError b


//    type PartitionerService(w : PartitionerServiceInfo) =

//        let ping() = failwith $"Not implemented yet - %A{w}"

//        interface IPartitionerService with
//            member _.ping() = ping()

//        interface IHostedService with
//            member _.StartAsync(cancellationToken : CancellationToken) =
//                async {
//                    printfn "PartitionerService::StartAsync..."
//                }
//                |> Async.StartAsTask
//                :> Task

//            member _.StopAsync(cancellationToken : CancellationToken) =
//                async {
//                    printfn "PartitionerService::StopAsync..."
//                }
//                |> Async.StartAsTask
//                :> Task


    let onProcessMessage (proxy : PartitionerProxy) (m : DistributedProcessingMessage) =
        printfn $"onProcessMessage: Starting. messageId: {m.messageDataInfo.messageId}."

        match m.messageData with
        | UserMsg (PartitionerMsg x) ->
            match x with
            //| RunModelWrkMsg (r, d) ->
            //    printfn $"    onProcessMessage: runQueueId: '{r}'."

            //    match proxy.saveModelData r d with
            //    | Ok() ->
            //        printfn $"    onProcessMessage: saveWorkerNodeRunModelData with runQueueId: '%A{r}' - OK."
            //        Ok()
            //    | Error e ->
            //        printfn $"    onProcessMessage: saveWorkerNodeRunModelData with runQueueId: '{r}' ERROR: %A{e}."
            //        let e1 = OnProcessMessageErr (CannotSaveModelDataErr (m.messageDataInfo.messageId, r))
            //        e1 + e |> Error
            //| CancelRunWrkMsg q -> q ||> proxy.requestCancellation
            //| RequestChartsWrkMsg q -> q ||> proxy.notifyOfResults
            | UpdateProgressPrtMsg p ->
                printfn $"    onProcessMessage: updateProgress: %A{p}."
                Ok()
            | SaveChartsPrtMsg c ->
                printfn $"    onProcessMessage: saveCharts: %A{c}."
                Ok()
            | RegisterWorkerNodePrtMsg w ->
                printfn $"    onProcessMessage: register: %A{w}."
                Ok()
            | UnregisterWorkerNodePrtMsg w ->
                printfn $"    onProcessMessage: unregister: %A{w}."
                Ok()
        | _ -> (m.messageDataInfo.messageId, m.messageData.getInfo()) |> InvalidMessageErr |> OnProcessMessageErr |> Error


    type PartitionerRunner (i : PartitionerContext) =
        let mutable eventHandlers = []

        let partitionerId = i.partitionerServiceInfo.partitionerInfo.partitionerId
        let messageProcessor = new MessagingClient<DistributedProcessingMessageData>(i.messagingClientData) :> IMessageProcessor<DistributedProcessingMessageData>

        let onProcessMessageImpl (m : DistributedProcessingMessage) =
            let r = onProcessMessage i.partitionerProxy m

            let r1 =
                match r with
                | Ok v -> Ok v
                | Error e ->
                    printfn $"WorkerNodeRunner - error: '{e}'."
                    OnGetMessagesErr FailedToProcessErr |> Error

            r1

        let onTryStart() =
            let toError e = failwith $"Error: '{e}'."

            match messageProcessor.tryStart() with
            | Ok () ->
                let getMessages() =
                    match messageProcessor.processMessages onProcessMessageImpl with
                    | Ok () -> Ok ()
                    | Error e -> CreateServiceImplWorkerNodeErr e |> Error

                let i1 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue toError getMessages "PartitionerRunner - getMessages"
                    //                let h = TimerEventHandler(TimerEventHandlerInfo.defaultValue logger getMessages "WorkerNodeRunner - getMessages")
                let h = TimerEventHandler i1
                do h.start()

                //// Attempt to restart solvers in case they did not start (due to whatever reason) or got killed.
                //let i2 = TimerEventHandlerInfo<DistributedProcessingError>.oneHourValue toError startSolvers "WorkerNodeRunner - start solvers"
                ////let s = ClmEventHandler(ClmEventHandlerInfo.oneHourValue logger w.start "WorkerNodeRunner - start")
                //let s = TimerEventHandler i2
                //do s.start()
                eventHandlers<- [ h ]

                Ok ()
            | Error e -> UnableToStartMessagingClientErr e |> Error 

        let onTryStop() =
            printfn "PartitionerRunner - stopping timers."
            eventHandlers |> List.iter (fun h -> h.stop())
            Ok()

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                match onTryStart() with
                | Ok () -> Task.CompletedTask
                | Error e -> 
                    printfn $"Error during start: %A{e}."
                    Task.FromException(new Exception($"Failed to start PartitionerRunner: %A{e}"))

            member _.StopAsync(cancellationToken: CancellationToken) =
                match onTryStop() with
                | Ok () -> Task.CompletedTask
                | Error e -> 
                    printfn $"Error during stop: %A{e}."
                    Task.CompletedTask // Log the error, but complete the task to allow the shutdown process to continue.
