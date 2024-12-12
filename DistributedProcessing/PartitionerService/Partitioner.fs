namespace Softellect.DistributedProcessing.PartitionerService

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.DistributedProcessing.AppSettings.PartitionerService
open Softellect.DistributedProcessing.Primitives.PartitionerService
open Softellect.DistributedProcessing.Errors
open Softellect.Wcf.Service
open CoreWCF
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.Proxy.PartitionerService
open Softellect.Messaging.Client
open Softellect.Messaging.Proxy
open Softellect.Sys.TimerEvents
open Softellect.Sys.Rop
open Softellect.DistributedProcessing.DataAccess.PartitionerService
open Softellect.DistributedProcessing.Messages

module Partitioner =

    let private toError g f = f |> g |> Error
    let private addError g f e = ((f |> g) + e) |> Error
    let private combineUnitResults = combineUnitResults DistributedProcessingError.addError


    let private saveResultsImpl l (r : ResultInfo)=
        match tryGetSolverName r.runQueueId with
        | Ok (Some s) ->
            let i =
                {
                    resultLocation = l
                    solverName = s
                }
            saveLocalResultInfo i r
        | Ok None -> r.runQueueId |> UnableToFindSolverNameErr |> TryGetSolverNameErr |> Error
        | Error e -> Error e
        |> Logger.logIfError


    type PartitionerProxy
        with
        static member create (i : PartitionerServiceInfo) : PartitionerProxy =
            {
                tryLoadFirstRunQueue = tryLoadFirstRunQueue
                tryGetAvailableWorkerNode = tryGetAvailableWorkerNode i.partitionerInfo.lastAllowedNodeErr
                upsertRunQueue = upsertRunQueue
                tryLoadRunQueue = tryLoadRunQueue
                upsertWorkerNodeInfo = upsertWorkerNodeInfo
                loadWorkerNodeInfo = loadWorkerNodeInfo i.partitionerInfo
                saveResults = saveResultsImpl i.partitionerInfo.resultLocation
                loadModelBinaryData = loadModelBinaryData
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
            match proxy.tryGetAvailableWorkerNode q.solverId with
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
        Logger.logTrace $"updateProgress: i = %A{i}"
        let addError = addError UpdateProgressRunnerErr
        let toError = toError UpdateProgressRunnerErr

        match proxy.tryLoadRunQueue i.runQueueId with
        | Ok (Some q) ->
            let q1 = { q with progressData = i.progressData }

            let upsert q2 =
                Logger.logTrace $"updateProgress.upsert: Upserting %A{i} into %A{q2}."

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
        Logger.logInfo $"register: r = %A{r}"
        proxy.upsertWorkerNodeInfo r |> bindError (addError RegisterRunnerErr (UnableToUpsertWorkerNodeInfoRunnerErr r.workerNodeId))


    let private unregister (proxy : PartitionerProxy) (r : WorkerNodeId) =
        Logger.logInfo $"unregister: r = %A{r}"
        let addError = addError UnregisterRunnerErr

        match proxy.loadWorkerNodeInfo r with
        | Ok w -> proxy.upsertWorkerNodeInfo { w with noOfCores = 0 } |> bindError (addError (UnableToUpsertWorkerNodeInfoOnUnregisterRunnerErr r))
        | Error e -> addError (UnableToLoadWorkerNodeInfoRunnerErr r) e


    let private saveResults (proxy : PartitionerProxy) (c : ResultInfo) =
        Logger.logInfo $"saveResults: c.runQueueId = %A{c.runQueueId}"
        proxy.saveResults c |> bindError (addError SaveResultsRunnerErr (UnableToSaveResultsRunnerErr c.runQueueId))


    // let updateSolverDeploymentInfo (proxy : PartitionerProxy) w s r =
    //     Logger.logInfo $"updateSolverDeploymentInfo: %A{w}, %A{r}."
    //
    //     match r with
    //     | Ok s -> failwith ""
    //     | Error e -> failwith ""


    type IPartitionerService =
        inherit IHostedService

        /// To check if service is working.
        abstract ping : unit -> DistributedProcessingUnitResult


    /// https://gist.github.com/dgfitch/661656
    [<ServiceContract(ConfigurationName = PartitionerWcfServiceName)>]
    type IPartitionerWcfService =
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
                    Logger.logInfo "PartitionerService::StartAsync..."
                }
                |> Async.StartAsTask
                :> Task

            member _.StopAsync(cancellationToken : CancellationToken) =
                async {
                    Logger.logInfo "PartitionerService::StopAsync..."
                }
                |> Async.StartAsTask
                :> Task


    let onProcessMessage (proxy : PartitionerProxy) (m : DistributedProcessingMessage) =
        Logger.logTrace $"onProcessMessage: Starting. messageId: '{m.messageDataInfo.messageId}', info: '{(m.messageData.getInfo())}'."

        match m.messageData with
        | UserMsg (PartitionerMsg x) ->
            match x with
            | UpdateProgressPrtMsg p -> updateProgress proxy p
            | SaveResultsPrtMsg c -> saveResults proxy c
            | RegisterWorkerNodePrtMsg w -> register proxy w
            | UnregisterWorkerNodePrtMsg w -> unregister proxy w
            | SolverDeploymentResultMsg (w, s, r) -> updateSolverDeploymentInfo w s r
        | _ -> (m.messageDataInfo.messageId, m.messageData.getInfo()) |> InvalidMessageErr |> OnProcessMessageErr |> Error


    type PartitionerRunner (ctx : PartitionerContext) =
        let mutable eventHandlers = []

        let messageProcessor = new MessagingClient<DistributedProcessingMessageData>(ctx.messagingClientData) :> IMessageProcessor<DistributedProcessingMessageData>

        let onProcessMessageImpl (m : DistributedProcessingMessage) =
            let r = onProcessMessage ctx.partitionerProxy m

            let r1 =
                match r with
                | Ok v -> Ok v
                | Error e ->
                    Logger.logError $"PartitionerRunner (onProcessMessageImpl) - ERROR processing message: messageId = '%A{m.messageDataInfo.messageId}', error = '%A{e}'."
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
            Logger.logInfo "PartitionerRunner - stopping timers."
            eventHandlers |> List.iter (fun h -> h.stop())
            Ok()

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                match onTryStart() with
                | Ok () -> Task.CompletedTask
                | Error e ->
                    Logger.logCrit $"Error during start: %A{e}."
                    Task.FromException(new Exception($"Failed to start PartitionerRunner: %A{e}"))

            member _.StopAsync(cancellationToken: CancellationToken) =
                match onTryStop() with
                | Ok () -> Task.CompletedTask
                | Error e ->
                    Logger.logError $"Error during stop: %A{e}."
                    Task.CompletedTask // Log the error, but complete the task to allow the shutdown process to continue.
