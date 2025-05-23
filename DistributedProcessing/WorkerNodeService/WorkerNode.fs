﻿namespace Softellect.DistributedProcessing.WorkerNodeService

open System
open System.Threading
open System.Threading.Tasks
open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Core
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Rop
open Softellect.Sys.TimerEvents
open Softellect.Messaging.Proxy
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.Proxy.WorkerNodeService
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.Messages
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


    let private tryDeploySolver  (i : WorkerNodeRunnerContext) (s : Solver) =
        let currentBuildNumber = BuildNumber.currentBuildNumber
        let proxy = i.workerNodeProxy
        let solverLocation = getSolverLocation i.workerNodeServiceInfo.workerNodeLocalInto s.solverName
        Logger.logTrace (fun () -> $"tryDeploySolver: %A{s.solverId}, %A{s.solverName}, solverLocation: '{solverLocation.value}'.")
        let toError e = e |> TryDeploySolverErr |> Error

        let install() =
            match proxy.reinstallWorkerNodeService solverLocation (getAssemblyLocation()) with
            | Ok() ->
                Logger.logTrace (fun () -> $"Worker node service reinstalled from location: '{solverLocation.value}'.")
                Ok()
            | Error e -> Error e

        let deploy install =
            match proxy.deleteSolverFolder solverLocation with
            | Ok () ->
                Logger.logTrace (fun () -> $"Solver %A{s.solverId}, solverLocation: '{solverLocation.value}' folder deleted.")
                match proxy.unpackSolver solverLocation s with
                | Ok () ->
                    Logger.logTrace (fun () -> $"Solver %A{s.solverId}, solverLocation: '{solverLocation.value}' unpacked.")
                    match proxy.copyAppSettings solverLocation with
                    | Ok() ->
                        Logger.logTrace (fun () -> $"Solver %A{s.solverId}, solverLocation: '{solverLocation.value}' appsettings copied.")
                        match proxy.setSolverDeployed s.solverId with
                        | Ok() -> install()
                        | Error e -> Error e
                    | Error e -> Error e
                | Error e -> Error e
            | Error e -> Error e


        match s.solverId = SolverId.workerNodeServiceId with
        | false ->
            let result =
                match proxy.checkSolverRunning s.solverName with
                | CanRun ->
                    Logger.logTrace (fun () -> $"Solver %A{s.solverId} is not running. Proceeding with deployment.")
                    deploy (fun () -> Ok())
                | TooManyRunning n ->
                    Logger.logWarn $"Cannot deploy because there are {n} solvers %A{s.solverName} running."
                    n |> CanNotDeployDueToRunningSolversErr |> toError
                | GetProcessesByNameExn e ->
                    Logger.logCrit $"Exception: %A{e}, %A{s.solverId}, %A{s.solverName}."
                    e |> TryDeploySolverExn |> toError
                | AlreadyRunning p ->
                    let m = $"This should never happen: %A{p}, %A{s.solverId}, %A{s.solverName}."
                    Logger.logCrit m
                    m |> TryDeploySolverCriticalErr |> toError

            notifyOfSolverDeployment i s.solverId result
        | true ->
            let notify result = notifyOfSolverDeployment i SolverId.workerNodeServiceId result

            match deploy install with
            | Ok () ->
                Logger.logTrace (fun () -> $"Checking build number...")

                match proxy.tryGetWorkerNodeReinstallationInfo() with
                | Ok (Some r) ->
                    match r = currentBuildNumber with
                    | true ->
                        Logger.logTrace (fun () -> $"Worker node service already reinstalled. Build number: %A{r}. Notifying Partitioner.")
                        Ok() |> notify // Already installed current build number.
                    | false ->
                        Logger.logTrace (fun () -> $"Worker node service registered build number: %A{r}, current build number %A{currentBuildNumber}.")
                        Ok() // Do not notify as reinstallation has not been performed yet.
                | Ok None ->
                    Logger.logTrace (fun () -> $"Worker node service does not have registered build number, current build number %A{currentBuildNumber}.")
                    Ok() // Do not notify as reinstallation has not been performed yet.
                | Error e ->
                    Logger.logError $"Error getting worker node reinstallation info: %A{e}."
                    Error e |> notify
            | Error e ->
                Logger.logError $"Error deploying worker node service: %A{e}."
                Error e |> notify


    let private notifyOfReinstallation (i : WorkerNodeRunnerContext) =
        let currentBuildNumber = BuildNumber.currentBuildNumber
        let proxy = i.workerNodeProxy

        let notify result =
            Logger.logTrace (fun () -> $"notifyOfReinstallation: %A{currentBuildNumber}, result: %A{result}.")
            notifyOfSolverDeployment i SolverId.workerNodeServiceId result

        let update() =
            Logger.logTrace (fun () -> $"notifyOfReinstallation: saving build number %A{currentBuildNumber}.")
            proxy.trySaveWorkerNodeReinstallationInfo currentBuildNumber

        match proxy.tryGetWorkerNodeReinstallationInfo() with
        | Ok (Some r) ->
            Logger.logTrace (fun () -> $"Worker node service registered build number: %A{r}, current build number %A{currentBuildNumber}.")

            match r = currentBuildNumber with
            | true ->
                Logger.logTrace (fun () -> $"Worker node service already reinstalled. Build number: %A{r}.")
                Ok() // Already reinstalled and notified.
            | false -> update() |> notify
        | Ok None -> update() |> notify
        | Error e -> Error e |> notify


    let private processSolver (i : WorkerNodeRunnerContext) (s : Solver) =
        Logger.logInfo $"processSolver: solverId: '{s.solverId}', solverName: '{s.solverName}'."

        let result =
            match i.workerNodeProxy.saveSolver s with
            | Ok() -> tryDeploySolver i s
            | Error e -> Error e

        match result with
        | Ok () -> Logger.logInfo $"Solver %A{s.solverId} was deployed successfully."
        | Error e -> Logger.logError $"Solver %A{s.solverId} deployment failed with error : %A{e}."

        result


    let startSolvers (i : WorkerNodeRunnerContext) numberOfCores =
        Logger.logTrace (fun () -> "startSolvers: Starting.")
        match i.workerNodeProxy.loadAllNotStartedRunQueueId i.workerNodeServiceInfo.workerNodeLocalInto.lastErrMinAgo with
        | Ok m ->
            Logger.logTrace (fun () -> $"startSolvers: m = '%A{m}'.")
            m
            |> List.map (i.workerNodeProxy.tryRunSolverProcess i.workerNodeProxy.tryRunSolverProcessProxy numberOfCores)
            |> List.map (fun e -> match e with | Ok _ -> Ok() | Error e -> Error e) // The solvers will store their PIDs in the database.
            |> foldUnitResults
        | Error e ->
            Logger.logError $"startSolvers: ERROR: '%A{e}'."
            Error e


    let private deployAllSolvers (i : WorkerNodeRunnerContext) =
        let proxy = i.workerNodeProxy

        match proxy.loadAllNotDeployedSolverId() with
        | Ok x ->
            let r =
                x
                |> List.map proxy.tryLoadSolver
                |> List.map (fun e -> e |> Result.bind (tryDeploySolver i))
                |> foldUnitResults

            Logger.logIfError r
        | Error e ->
            Logger.logError $"Error loading not deployed solvers: %A{e}"
            Error e


    let onProcessMessage (i : WorkerNodeRunnerContext) (m : DistributedProcessingMessage) =
        Logger.logTrace (fun () -> $"onProcessMessage: Starting. messageId: {m.messageDataInfo.messageId}, info: '{m.messageData.getInfo()}'.")
        let proxy = i.workerNodeProxy

        match m.messageData with
        | UserMsg (WorkerNodeMsg x) ->
            match x with
            | RunModelWrkMsg (r, s, d) ->
                Logger.logTrace (fun () -> $"    onProcessMessage: runQueueId: '{r}'.")

                match proxy.saveModelData r s d with
                | Ok() ->
                    Logger.logTrace (fun () -> $"    onProcessMessage: saveWorkerNodeRunModelData with runQueueId: '%A{r}' - OK.")
                    Ok()
                | Error e ->
                    Logger.logError (fun () -> $"    onProcessMessage: saveWorkerNodeRunModelData with runQueueId: '{r}' ERROR: %A{e}.")
                    let e1 = OnProcessMessageErr (CannotSaveModelDataErr (m.messageDataInfo.messageId, r))
                    e1 + e |> Error
            | CancelRunWrkMsg q ->
                Logger.logTrace (fun () -> $"    onProcessMessage: CancelRunWrkMsg with runQueueId: '%A{q}'.")
                let result = q ||> proxy.requestCancellation
                Logger.logInfo $"    onProcessMessage: CancelRunWrkMsg with runQueueId: '%A{q}' - result: '%A{result}'."
                result
            | RequestResultsWrkMsg q ->
                Logger.logTrace (fun () -> $"    onProcessMessage: RequestResultsWrkMsg with runQueueId: '%A{q}'.")
                let result = q ||> proxy.notifyOfResults
                Logger.logTrace (fun () -> $"    onProcessMessage: RequestResultsWrkMsg with runQueueId: '%A{q}' - result: '%A{result}'.")
                result
            | UpdateSolverWrkMsg e ->
                match proxy.tryDecryptSolver e (m.messageDataInfo.sender |> PartitionerId) with
                | Ok s -> processSolver i s
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

        let startSolvers() = startSolvers i numberOfCores
        let deployAllSolvers() = deployAllSolvers i
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

        // let sr n (q : RunQueueId) =
        //     match i.workerNodeProxy.tryRunSolverProcess n q with
        //     | Ok _ -> Ok() // The solver will store PID in the database.
        //     | Error e -> (q |> CannotRunModelErr |> OnRunModelErr) + e |> Error

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

                        let i1 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue toError processMessages "WorkerNodeRunner - processMessages."
                        let h = TimerEventHandler i1
                        do h.start()

                        // Attempt to restart solvers in case they did not start (due to whatever reason) or got killed.
                        // Use oneHourValue from appsettings.json.
                        let i2 = TimerEventHandlerInfo<DistributedProcessingError>.oneMinuteValue toError startSolvers "WorkerNodeRunner - start solvers."
                        let s = TimerEventHandler i2
                        do s.start()

                        // Attempt to deploy all solvers in case they were not deployed.
                        // Use oneHourValue from appsettings.json.
                        let i3 = TimerEventHandlerInfo<DistributedProcessingError>.oneMinuteValue toError deployAllSolvers "WorkerNodeRunner - deploy solvers."
                        let d = TimerEventHandler i3
                        do d.start()

                        eventHandlers <- [ h; s; d ]
                        notifyOfReinstallation i
                    | Error e -> UnableToStartMessagingClientErr e |> Error
                | Error e -> Error e
            | true ->
                Logger.logInfo "WorkerNodeRunner: Unregistering..."
                match onUnregisterImpl() with
                | Ok() -> failwith "WorkerNodeRunner - onUnregisterImpl for inactive worker node is not implemented yet."
                | Error e -> CreateServiceImplWorkerNodeErr e |> Error

        let onTryStop() =
            Logger.logInfo "WorkerNodeRunner - stopping timers."
            eventHandlers |> List.iter _.stop()
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
