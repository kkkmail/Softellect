﻿namespace Softellect.DistributedProcessing.WorkerNodeService

open System
open System.Threading
open System.Threading.Tasks
open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Logging
open Softellect.Sys.Rop
open Softellect.Sys.TimerEvents
open Softellect.Messaging.Proxy
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.Proxy.WorkerNodeService
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.Messages
open Softellect.Sys.Primitives
open Softellect.Messaging.Client
open Microsoft.Extensions.Hosting

module WorkerNode =

    type WorkerNodeMonitorResponse =
        | CannotAccessWrkNode
        | ErrorOccurred of DistributedProcessingError

        override this.ToString() =
            match this with
            | CannotAccessWrkNode -> "Cannot access worker node."
            | ErrorOccurred e -> "Error occurred: " + e.ToString()


    /// The head should contain the latest error and the tail the earliest error.
    let private foldUnitResults r = foldUnitResults DistributedProcessingError.addError r


    let private processSolver (i : WorkerNodeRunnerContext) f (s : Solver) =
        Logger.logInfo $"onProcessMessage: solverId: '{s.solverId}', solverName: '{s.solverName}'."
        let proxy = i.workerNodeProxy

        let result =
            match proxy.saveSolver s with
            | Ok() ->
                match proxy.unpackSolver f s with
                | Ok () ->
                    match proxy.setSolverDeployed s.solverId with
                    | Ok () ->
                        Logger.logInfo $"Solver %A{s.solverId} deployed successfully."
                        Ok()
                    | Error e -> Error e
                | Error e -> e |> Error
            | Error e -> Error e

        notifyOfSolverDeployment i s.solverId result


    let onProcessMessage (i : WorkerNodeRunnerContext) (m : DistributedProcessingMessage) =
        Logger.logTrace $"onProcessMessage: Starting. messageId: {m.messageDataInfo.messageId}, info: '{m.messageData.getInfo()}'."
        let proxy = i.workerNodeProxy

        match m.messageData with
        | UserMsg (WorkerNodeMsg x) ->
            match x with
            | RunModelWrkMsg (r, s, d) ->
                Logger.logTrace $"    onProcessMessage: runQueueId: '{r}'."

                match proxy.saveModelData r s d with
                | Ok() ->
                    Logger.logTrace $"    onProcessMessage: saveWorkerNodeRunModelData with runQueueId: '%A{r}' - OK."
                    Ok()
                | Error e ->
                    Logger.logError $"    onProcessMessage: saveWorkerNodeRunModelData with runQueueId: '{r}' ERROR: %A{e}."
                    let e1 = OnProcessMessageErr (CannotSaveModelDataErr (m.messageDataInfo.messageId, r))
                    e1 + e |> Error
            | CancelRunWrkMsg q ->
                Logger.logTrace $"    onProcessMessage: CancelRunWrkMsg with runQueueId: '%A{q}'."
                let result = q ||> proxy.requestCancellation
                Logger.logInfo $"    onProcessMessage: CancelRunWrkMsg with runQueueId: '%A{q}' - result: '%A{result}'."
                result
            | RequestResultsWrkMsg q ->
                Logger.logTrace $"    onProcessMessage: RequestResultsWrkMsg with runQueueId: '%A{q}'."
                let result = q ||> proxy.notifyOfResults
                Logger.logTrace $"    onProcessMessage: RequestResultsWrkMsg with runQueueId: '%A{q}' - result: '%A{result}'."
                result
            | UpdateSolverWrkMsg e ->
                match proxy.tryDecryptSolver e (m.messageDataInfo.sender |> PartitionerId) with
                | Ok s -> processSolver i (getSolverLocation i.workerNodeServiceInfo.workerNodeLocalInto s.solverName) s
                | Error e -> Error e
        | _ -> (m.messageDataInfo.messageId, m.messageData.getInfo()) |> InvalidMessageErr |> OnProcessMessageErr |> Error


    type WorkerNodeRunner (i : WorkerNodeRunnerContext) =
        let mutable started = false
        let mutable eventHandlers = []

        let numberOfCores = i.workerNodeServiceInfo.workerNodeInfo.noOfCores
        let partitionerId = i.workerNodeServiceInfo.workerNodeInfo.partitionerId
        let messageProcessor = MessagingClient<DistributedProcessingMessageData>(i.messagingClientData) :> IMessageProcessor<DistributedProcessingMessageData>

        let onProcessMessageImpl (m : DistributedProcessingMessage) =
            let r = onProcessMessage i m

            let r1 =
                match r with
                | Ok v -> Ok v
                | Error e ->
                    Logger.logError $"WorkerNodeRunner - ERROR: '{e}'."
                    OnGetMessagesErr FailedToProcessErr |> Error

            r1

        let startSolvers() =
            Logger.logTrace "startSolvers: Starting."
            match i.workerNodeProxy.loadAllActiveRunQueueId() with
            | Ok m ->
                Logger.logTrace $"startSolvers: m = '%A{m}'."
                m
                |> List.map (i.workerNodeProxy.tryRunSolverProcess numberOfCores)
                |> List.map (fun e -> match e with | Ok _ -> Ok() | Error e -> Error e) // The solvers will store their PIDs in the database.
                |> foldUnitResults
            | Error e ->
                Logger.logError $"startSolvers: ERROR: '%A{e}'."
                Error e

        let onStartImpl() = if not started then startSolvers() else Ok()

        let onRegisterImpl() =
            let result =
                {
                    partitionerRecipient = partitionerId
                    deliveryType = GuaranteedDelivery
                    messageData = i.workerNodeServiceInfo.workerNodeInfo |> RegisterWorkerNodePrtMsg
                }.getMessageInfo()
                |> messageProcessor.sendMessage

            match result with
            | Ok v -> Ok v
            | Error e -> e |> UnableToRegisterWorkerNodeErr |> Error

        let onUnregisterImpl() =
            let result =
                {
                    partitionerRecipient = partitionerId
                    deliveryType = GuaranteedDelivery
                    messageData = i.workerNodeServiceInfo.workerNodeInfo.workerNodeId |> UnregisterWorkerNodePrtMsg
                }.getMessageInfo()
                |> messageProcessor.sendMessage

            result

        let sr n (q : RunQueueId) =
            match i.workerNodeProxy.tryRunSolverProcess n q with
            | Ok _ -> Ok() // The solver will store PID in the database.
            | Error e -> (q |> CannotRunModelErr |> OnRunModelErr) + e |> Error

        let onTryStart() =
            let toError e =
                Logger.logError $"Error: '{e}'."
                TimerEventErr e

            match i.workerNodeServiceInfo.workerNodeInfo.isInactive with
            | false ->
                Logger.logInfo "WorkerNodeRunner: Registering..."
                match onRegisterImpl >-> onStartImpl |> evaluate with
                | Ok() ->
                    match messageProcessor.tryStart() with
                    | Ok () ->
                        let processMessages() =
                            match messageProcessor.processMessages onProcessMessageImpl with
                            | Ok () -> Ok ()
                            | Error e -> CreateServiceImplWorkerNodeErr e |> Error

                        let i1 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue toError processMessages "WorkerNodeRunner - processMessages"
                        let h = TimerEventHandler i1
                        do h.start()

                        // Attempt to restart solvers in case they did not start (due to whatever reason) or got killed.
                        // Use oneHourValue
                        let i2 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue toError startSolvers "WorkerNodeRunner - start solvers"
                        let s = TimerEventHandler i2
                        do s.start()
                        eventHandlers<- [ h; s ]

                        Ok ()
                    | Error e -> UnableToStartMessagingClientErr e |> Error
                | Error e -> Error e
            | true ->
                Logger.logInfo "WorkerNodeRunner: Unregistering..."
                match onUnregisterImpl() with
                | Ok() -> failwith "WorkerNodeRunner - onUnregisterImpl for inactive worker node is not implemented yet."
                | Error e -> CreateServiceImplWorkerNodeErr e |> Error

        let onTryStop() =
            Logger.logInfo "WorkerNodeRunner - stopping timers."
            eventHandlers |> List.iter (fun h -> h.stop())
            Ok()

        interface IHostedService with

            /// TODO kk:20241027 - If some solvers failed to start then we need to send a message to Partitioner to notify it.
            member _.StartAsync(cancellationToken: CancellationToken) =
                match onTryStart() with
                | Ok () -> Task.CompletedTask
                | Error e ->
                    Logger.logCrit $"Error during start: %A{e}."
                    Task.FromException(Exception($"Failed to start WorkerNodeRunner: %A{e}"))

            member _.StopAsync(cancellationToken: CancellationToken) =
                match onTryStop() with
                | Ok () -> Task.CompletedTask
                | Error e ->
                    Logger.logError $"Error during stop: %A{e}."
                    Task.CompletedTask // Log the error, but complete the task to allow the shutdown process to continue.
