namespace Softellect.DistributedProcessing

//open Primitives.GeneralPrimitives
//open Primitives.SolverPrimitives
open Softellect.Sys.Rop
open Softellect.Messaging.Primitives
open Softellect.Messaging.Client
open Softellect.Messaging.Proxy

//open Clm.ModelParams
open Softellect.Messaging.ServiceInfo
//open ContGenServiceInfo.ServiceInfo
//open ClmSys.ClmErrors
//open ClmSys.ContGenPrimitives
//open ClmSys.TimerEvents
//open ClmSys.WorkerNodeData
//open ServiceProxy.ModelRunnerProxy
//open ClmSys.ModelRunnerErrors
//open MessagingServiceInfo.ServiceInfo
//open ClmSys.WorkerNodePrimitives
//open ClmSys.Logging
//open DbData.DatabaseTypesDbo
//open DbData.DatabaseTypesClm
//open ServiceProxy.MsgProcessorProxy
open Softellect.Messaging.Errors
//open ModelGenerator


module Partitioner =

    let private printDebug s = printfn $"{s}"
//    let private printDebug s = ()

    let private toError g f = f |> g |> ModelRunnerErr |> Error
    let private addError g f e = ((f |> g |> ModelRunnerErr) + e) |> Error
    let private maxMessages = [ for _ in 1..maxNumberOfMessages -> () ]

    type OnProcessMessageType = OnProcessMessageType<unit>
    type OnGetMessagesProxy = OnGetMessagesProxy<unit>
    let onGetMessages = onGetMessages<unit>


    let runModel (proxy : RunModelProxy) (q : RunQueue) : UnitResult =
        match q.toMessageInfoOpt proxy.loadModelData proxy.controlData with
        | Ok (Some m) ->
            match proxy.sendRunModelMessage m with
            | Ok v -> Ok v
            | Error e -> MessagingRunnerErr e |> toError RunModelRunnerErr
        | Ok None -> q.runQueueId |> MissingWorkerNodeRunnerErr |> toError RunModelRunnerErr
        | Error e -> (addError RunModelRunnerErr) (UnableToLoadModelDataRunnerErr (q.runQueueId, q.info.modelDataId )) e


    /// Tries to run the first not scheduled run queue entry using the first available worker node.
    let tryRunFirstModel (proxy : TryRunFirstModelProxy) =
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


    let tryCancelRunQueue (proxy : TryCancelRunQueueProxy) (q, c) =
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


    let tryRequestResults (proxy : TryRequestResultsProxy) (q, c) =
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

                        messageData = (q, c) |> RequestResultWrkMsg |> WorkerNodeMsg |> UserMsg
                    }
                    |> proxy.sendRequestResultsMessage

                match r1 with
                | Ok v -> Ok v
                | Error e -> MessagingTryRequestResultsRunnerErr e |> toError
            | None -> Ok()
        | Ok None -> toError (TryRequestResultsRunnerError.TryLoadRunQueueRunnerErr q)
        | Error e -> addError (TryRequestResultsRunnerError.TryLoadRunQueueRunnerErr q) e


    let tryReset (proxy : TryResetProxy) q =
        proxy.tryResetRunQueue q


    /// Tries to run all available work items (run queue) on all available work nodes until one or the other is exhausted.
    let tryRunAllModels (proxy : TryRunAllModelsProxy) =
        let rec doWork() =
            match proxy.tryRunFirstModel() with
            | Ok r ->
                match r with
                | WorkScheduled -> doWork()
                | NoWork -> Ok()
                | NoAvailableWorkerNodes -> Ok()
            | Error e -> addError TryRunAllModelsRunnerErr UnableToTryRunFirstModelRunnerErr e

        doWork()


    let updateProgress (proxy : UpdateProgressProxy) (i : ProgressUpdateInfo) =
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


    let register (proxy : RegisterProxy) (r : WorkerNodeInfo) =
        //printfn "register: r = %A" r
        proxy.upsertWorkerNodeInfo r |> bindError (addError RegisterRunnerErr (UnableToUpsertWorkerNodeInfoRunnerErr r.workerNodeId))


    let unregister (proxy : UnregisterProxy) (r : WorkerNodeId) =
        //printfn "unregister: r = %A" r
        let addError = addError UnregisterRunnerErr

        match proxy.loadWorkerNodeInfo r with
        | Ok w -> proxy.upsertWorkerNodeInfo { w with noOfCores = 0 } |> bindError (addError (UnableToUpsertWorkerNodeInfoOnUnregisterRunnerErr r))
        | Error e -> addError (UnableToLoadWorkerNodeInfoRunnerErr r) e


    let saveCharts (proxy : SaveChartsProxy) (c : ChartInfo) =
        printfn $"saveCharts: c.runQueueId = %A{c.runQueueId}"
        proxy.saveCharts c |> bindError (addError SaveChartsRunnerErr (UnableToSaveChartsRunnerErr c.runQueueId))


    let processMessage (proxy : ProcessMessageProxy) (m : Message) =
        printfn $"processMessage: messageId = %A{m.messageDataInfo.messageId}, message = %A{m}."

        let r =
            match m.messageData with
            | UserMsg (PartitionerMsg x) ->
                match x with
                | UpdateProgressPrtMsg i -> proxy.updateProgress i
                | SaveChartsPrtMsg c -> proxy.saveCharts c
                | RegisterWorkerNodePrtMsg r -> proxy.register r
                | UnregisterWorkerNodePrtMsg r -> proxy.unregister r
                |> bindError (addError ProcessMessageRunnerErr (ErrorWhenProcessingMessageRunnerErr m.messageDataInfo.messageId))
            | _ -> toError ProcessMessageRunnerErr (InvalidMessageTypeRunnerErr m.messageDataInfo.messageId)

        match r with
        | Ok() -> ()
        | Error e -> printfn $"processMessage: messageId = %A{m.messageDataInfo.messageId}, ERROR = %A{e}."

        r


    let getRunState (proxy : GetRunStateProxy) =
        let w, e = proxy.loadRunQueueProgress() |> unzipListResult
        w, e |> foldToUnitResult

