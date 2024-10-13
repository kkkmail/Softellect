namespace Softellect.DistributedProcessing.PartitionerService

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
open Softellect.DistributedProcessing.PartitionerService.Primitives
open Softellect.DistributedProcessing.DataAccess.PartitionerService

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

    type PartitionerProxy
        with
        static member create (i : PartitionerServiceInfo) : PartitionerProxy =
            {
                tryLoadFirstRunQueue = tryLoadFirstRunQueue
                tryGetAvailableWorkerNode = fun () -> tryGetAvailableWorkerNode i.partitionerInfo.lastAllowedNodeErr
                upsertRunQueue = upsertRunQueue
                tryLoadRunQueue = tryLoadRunQueue
                upsertWorkerNodeInfo = upsertWorkerNodeInfo
                loadWorkerNodeInfo = loadWorkerNodeInfo i.partitionerInfo.partitionerId
                saveCharts = saveLocalChartInfo (Some (i.partitionerInfo.resultLocation, None))
                loadModelBinaryData = loadModelBinaryData

                //runModel = failwith "PartitionerProxy: runModel is not implemented yet."
                //tryRunFirstModel = failwith "PartitionerProxy: tryRunFirstModel is not implemented yet."

                //sendRunModelMessage = failwith "PartitionerProxy: sendRunModelMessage is not implemented yet."
                //sendRequestResultsMessage = failwith "PartitionerProxy: sendRequestResultsMessage is not implemented yet."
                //tryResetRunQueue = failwith "PartitionerProxy: tryResetRunQueue is not implemented yet."
                //sendCancelRunQueueMessage = failwith "PartitionerProxy: sendCancelRunQueueMessage is not implemented yet."
            }


    let private toMessageInfoOpt loadModelBinaryData w (q : RunQueue) =
        match loadModelBinaryData q.runQueueId with
        | Ok m ->
            {
                workerNodeRecipient = w
                deliveryType = GuaranteedDelivery
                messageData = (q.runQueueId, q.solverId, m) |> RunModelWrkMsg
            }.getMessageInfo()
            |> Some |> Ok
        | Error e -> Error e


    let sendRunModelMessage (messageProcessor : IMessageProcessor<DistributedProcessingMessageData>) m =
        match messageProcessor.sendMessage m with
        | Ok v -> Ok v
        | Error e -> SendRunModelMessageErr e |> Error


    let private runModel messageProcessor (proxy : PartitionerProxy) workerNodeId (q : RunQueue) =
        match toMessageInfoOpt proxy.loadModelBinaryData workerNodeId q with
        | Ok (Some m) ->
            match sendRunModelMessage messageProcessor m with
            | Ok v -> Ok v
            | Error e -> addError RunModelRunnerErr MessagingRunnerErr e
        | Ok None -> q.runQueueId |> MissingWorkerNodeRunnerErr |> toError RunModelRunnerErr
        | Error e -> (addError RunModelRunnerErr) (UnableToLoadModelDataRunnerErr q.runQueueId) e


    /// Tries to run the first not scheduled run queue entry using the first available worker node.
    let private tryRunFirstModel messageProcessor (proxy : PartitionerProxy) =
        let addError = addError TryRunFirstModelRunnerErr

        match proxy.tryLoadFirstRunQueue() with
        | Ok (Some q) ->
            match proxy.tryGetAvailableWorkerNode() with
            | Ok (Some workerNodeId) ->
                let q1 = { q with workerNodeIdOpt = Some workerNodeId; runQueueStatus = RunRequestedRunQueue }

                match proxy.upsertRunQueue q1 with
                | Ok() ->
                    match runModel messageProcessor proxy workerNodeId q1 with
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


    // These are for PartitionerAdm
    //let tryCancelRunQueue (proxy : PartitionerProxy) (q, c) =
    //    let addError = addError TryCancelRunQueueRunnerErr
    //    let toError = toError TryCancelRunQueueRunnerErr

    //    match proxy.tryLoadRunQueue q with
    //    | Ok (Some r) ->
    //        let r1 =
    //            match r.workerNodeIdOpt with
    //            | Some w ->
    //                let r11 =
    //                    {
    //                        recipientInfo =
    //                            {
    //                                recipient = w.messagingClientId
    //                                deliveryType = GuaranteedDelivery
    //                            }

    //                        messageData = (q, c) |> CancelRunWrkMsg |> WorkerNodeMsg |> UserMsg
    //                    }
    //                    |> proxy.sendCancelRunQueueMessage

    //                match r11 with
    //                | Ok v -> Ok v
    //                | Error e -> TryCancelRunQueueRunnerError.MessagingTryCancelRunQueueRunnerErr e |> toError
    //            | None -> Ok()

    //        let r2 =
    //            match r.runQueueStatus with
    //            | NotStartedRunQueue -> { r with runQueueStatus = CancelledRunQueue } |> proxy.upsertRunQueue
    //            | RunRequestedRunQueue -> { r with runQueueStatus = CancelRequestedRunQueue } |> proxy.upsertRunQueue
    //            | InProgressRunQueue -> { r with runQueueStatus = CancelRequestedRunQueue } |> proxy.upsertRunQueue
    //            | CancelRequestedRunQueue -> { r with runQueueStatus = CancelRequestedRunQueue } |> proxy.upsertRunQueue
    //            | _ -> q |> TryCancelRunQueueRunnerError.InvalidRunQueueStatusRunnerErr |> toError

    //        combineUnitResults r1 r2
    //    | Ok None -> toError (TryCancelRunQueueRunnerError.TryLoadRunQueueRunnerErr q)
    //    | Error e -> addError (TryCancelRunQueueRunnerError.TryLoadRunQueueRunnerErr q) e


    //let tryRequestResults (proxy : PartitionerProxy) (q, c) =
    //    let addError = addError TryRequestResultsRunnerErr
    //    let toError = toError TryRequestResultsRunnerErr

    //    match proxy.tryLoadRunQueue q with
    //    | Ok (Some r) ->
    //        match r.workerNodeIdOpt with
    //        | Some w ->
    //            let r1 =
    //                {
    //                    recipientInfo =
    //                        {
    //                            recipient = w.messagingClientId
    //                            deliveryType = GuaranteedDelivery
    //                        }

    //                    messageData = (q, c) |> RequestChartsWrkMsg |> WorkerNodeMsg |> UserMsg
    //                }
    //                |> proxy.sendRequestResultsMessage

    //            match r1 with
    //            | Ok v -> Ok v
    //            | Error e -> MessagingTryRequestResultsRunnerErr e |> toError
    //        | None -> Ok()
    //    | Ok None -> toError (TryRequestResultsRunnerError.TryLoadRunQueueRunnerErr q)
    //    | Error e -> addError (TryRequestResultsRunnerError.TryLoadRunQueueRunnerErr q) e


    //let tryReset (proxy : PartitionerProxy) q =
    //    proxy.tryResetRunQueue q


    /// Tries to run all available work items (run queue) on all available work nodes until one or the other is exhausted.
    let private tryRunAllModels messageProcessor (proxy : PartitionerProxy) =
        let rec doWork() =
            match tryRunFirstModel messageProcessor proxy with
            | Ok r ->
                match r with
                | WorkScheduled -> doWork()
                | NoWork -> Ok()
                | NoAvailableWorkerNodes -> Ok()
            | Error e -> addError TryRunAllModelsRunnerErr UnableToTryRunFirstModelRunnerErr e

        doWork()


    let private updateProgress (proxy : PartitionerProxy) (i : ProgressUpdateInfo) =
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


    let private register (proxy : PartitionerProxy) (r : WorkerNodeInfo) =
        printfn "register: r = %A" r
        proxy.upsertWorkerNodeInfo r |> bindError (addError RegisterRunnerErr (UnableToUpsertWorkerNodeInfoRunnerErr r.workerNodeId))


    let private unregister (proxy : PartitionerProxy) (r : WorkerNodeId) =
        printfn "unregister: r = %A" r
        let addError = addError UnregisterRunnerErr

        match proxy.loadWorkerNodeInfo r with
        | Ok w -> proxy.upsertWorkerNodeInfo { w with noOfCores = 0 } |> bindError (addError (UnableToUpsertWorkerNodeInfoOnUnregisterRunnerErr r))
        | Error e -> addError (UnableToLoadWorkerNodeInfoRunnerErr r) e


    let private saveCharts (proxy : PartitionerProxy) (c : ChartInfo) =
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

        //abstract tryCancelRunQueue : RunQueueId -> CancellationType -> DistributedProcessingUnitResult
        //abstract tryRequestResults : RunQueueId -> DistributedProcessingUnitResult -> DistributedProcessingUnitResult
        //abstract tryReset : RunQueueId -> DistributedProcessingUnitResult

        /// To check if service is working.
        abstract ping : unit -> DistributedProcessingUnitResult


    /// https://gist.github.com/dgfitch/661656
    [<ServiceContract(ConfigurationName = PartitionerWcfServiceName)>]
    type IPartitionerWcfService =
        //[<OperationContract(Name = "tryCancelRunQueue")>]
        //abstract tryCancelRunQueue : q:byte[] -> byte[]

        //[<OperationContract(Name = "tryRequestResults")>]
        //abstract tryRequestResults : q:byte[] -> byte[]

        //[<OperationContract(Name = "tryReset")>]
        //abstract tryReset : q:byte[] -> byte[]

        [<OperationContract(Name = "ping")>]
        abstract ping : q:byte[] -> byte[]



    type PartitionerWcfService(w : IPartitionerService) =
        let toPingError f = f |> PrtPingWcfErr |> PartitionerWcfErr

        let ping() = w.ping()

        interface IPartitionerWcfService with
            member _.ping b = tryReply ping toPingError b


    type PartitionerService(w : PartitionerServiceInfo) =

        let ping() = failwith $"Not implemented yet - %A{w}"

        interface IPartitionerService with
            member _.ping() = ping()

        interface IHostedService with
            member _.StartAsync(cancellationToken : CancellationToken) =
                async {
                    printfn "PartitionerService::StartAsync..."
                }
                |> Async.StartAsTask
                :> Task

            member _.StopAsync(cancellationToken : CancellationToken) =
                async {
                    printfn "PartitionerService::StopAsync..."
                }
                |> Async.StartAsTask
                :> Task


    let onProcessMessage (proxy : PartitionerProxy) (m : DistributedProcessingMessage) =
        printfn $"onProcessMessage: Starting. messageId: {m.messageDataInfo.messageId}."

        match m.messageData with
        | UserMsg (PartitionerMsg x) ->
            match x with
            | UpdateProgressPrtMsg p -> updateProgress proxy p
            | SaveChartsPrtMsg c -> saveCharts proxy c
            | RegisterWorkerNodePrtMsg w -> register proxy w
            | UnregisterWorkerNodePrtMsg w -> unregister proxy w
        | _ -> (m.messageDataInfo.messageId, m.messageData.getInfo()) |> InvalidMessageErr |> OnProcessMessageErr |> Error


    type PartitionerRunner (ctx : PartitionerContext) =
        let mutable eventHandlers = []

        //let partitionerId = ctx.partitionerServiceInfo.partitionerInfo.partitionerId
        let messageProcessor = new MessagingClient<DistributedProcessingMessageData>(ctx.messagingClientData) :> IMessageProcessor<DistributedProcessingMessageData>

        let onProcessMessageImpl (m : DistributedProcessingMessage) =
            let r = onProcessMessage ctx.partitionerProxy m

            let r1 =
                match r with
                | Ok v -> Ok v
                | Error e ->
                    printfn $"PartitionerRunner - error: '{e}'."
                    OnGetMessagesErr FailedToProcessErr |> Error

            r1

        let distributeWork() = tryRunAllModels messageProcessor ctx.partitionerProxy

        let onTryStart() =
            let toError e = failwith $"Error: '{e}'."

            match messageProcessor.tryStart() with
            | Ok () ->
                let processMessages() =
                    match messageProcessor.processMessages onProcessMessageImpl with
                    | Ok () -> Ok ()
                    | Error e -> CreateServiceImplWorkerNodeErr e |> Error

                let i1 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue toError processMessages "PartitionerRunner - processMessages"
                let h = TimerEventHandler i1
                do h.start()

                // Distribute work - change to oneHourValue or configurable value.
                let i2 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue toError distributeWork "PartitionerRunner - distributeWork"
                let s = TimerEventHandler i2
                do s.start()
                eventHandlers<- [ h; s ]

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
