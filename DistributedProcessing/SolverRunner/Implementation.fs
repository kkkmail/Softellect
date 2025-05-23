﻿namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.Errors
open Softellect.Sys.Rop
open Softellect.Messaging.Primitives
open Softellect.Messaging.Client
open System.Diagnostics
open Softellect.Messaging.DataAccess
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.Proxy.SolverRunner
open Softellect.DistributedProcessing.DataAccess.SolverRunner
open Softellect.DistributedProcessing.SolverRunner.NoSql
open Softellect.DistributedProcessing.SolverRunner.Runner
open Softellect.Sys.Core
open Softellect.DistributedProcessing.AppSettings.SolverRunner
open Softellect.DistributedProcessing.VersionInfo
open Softellect.DistributedProcessing.Messages
open Softellect.Analytics.AppSettings
open Softellect.Analytics.Wolfram

module Implementation =

    let solverProgramName = ServiceName "SolverRunner"

    /// Extra solver's "overhead" allowed when running SolverRunner by hands.
    /// This is needed when two versions share the same machine and one version has some stuck
    /// work, which needs to be started.
    ///
    /// WorkNodeService does not use that overhead and so it will not start more than allowed.
    [<Literal>]
    let AllowedOverhead = 0.20


    /// Send the message directly to local database.
    let private sendMessage messagingDataVersion m i =
        createMessage messagingDataVersion m i
        |> saveMessage<DistributedProcessingMessageData> messagingDataVersion


    let private tryStartRunQueue q =
        let pid = ProcessId.getCurrentProcessId()
        let result = tryStartRunQueue q pid
        Logger.logTrace (fun () -> $"tryStartRunQueue: runQueueId = %A{q}, result = %A{result}.")
        result


    let private getAllowedSolvers i =
        let noOfCores = i.noOfCores
        max ((float noOfCores) * (1.0 + AllowedOverhead) |> int) (noOfCores + 1)


    let private onSaveResults<'P> (data : RunnerData) c =
        let i =
            {
                results = c
                runQueueId = data.runQueueId
                optionalFolder = data.optionalFolder
            }

        Logger.logTrace (fun () -> $"onSaveResults: Sending results with runQueueId = %A{data.runQueueId}, c.Length = %A{c.Length}.")

        let result =
            {
                partitionerRecipient = data.partitionerId
                deliveryType = GuaranteedDelivery
                messageData = i |> SaveResultsPrtMsg
            }.getMessageInfo()
            |> sendMessage data.messagingDataVersion data.workerNodeId.messagingClientId

        match result with
        | Ok v -> Ok v
        | Error e -> OnSaveResultsErr (SendResultMessageErr (data.partitionerId.messagingClientId, data.runQueueId, e)) |> Error


    let private toDeliveryType (p : ProgressUpdateInfo<'P>) =
        match p.updatedRunQueueStatus with
        | Some s ->
            match s with
            | NotStartedRunQueue -> (GuaranteedDelivery, false)
            | InactiveRunQueue -> (GuaranteedDelivery, false)
            | RunRequestedRunQueue -> (GuaranteedDelivery, false)
            | InProgressRunQueue -> (NonGuaranteedDelivery, false)
            | CompletedRunQueue -> (GuaranteedDelivery, true)
            | FailedRunQueue -> (GuaranteedDelivery, true)
            | CancelRequestedRunQueue -> (GuaranteedDelivery, false)
            | CancelledRunQueue -> (GuaranteedDelivery, true)

        | None -> (NonGuaranteedDelivery, false)


    let private onUpdateProgress<'P> (data : RunnerData) cb (pd : ProgressData<'P>) =
        let p =
            {
                runQueueId = data.runQueueId
                updatedRunQueueStatus =
                    match cb with
                    | RegularCallBack -> Some InProgressRunQueue
                    | FinalCallBack f ->
                        match f with
                        | CompletedCalculation -> Some CompletedRunQueue
                        | CancelledCalculation c ->
                            match c with
                            | CancelWithResults _ -> Some CancelledRunQueue
                            | AbortCalculation _ -> Some CancelledRunQueue
                progressData = pd
            }

        Logger.logTrace (fun () -> $"onUpdateProgress: runQueueId = %A{p.runQueueId}, updatedRunQueueStatus = %A{p.updatedRunQueueStatus}, progress = %A{p.progressData}.")
        let t, completed = toDeliveryType p
        let r0 = tryUpdateProgress<'P> p.runQueueId p.progressData

        let r11 =
            {
                partitionerRecipient = data.partitionerId
                deliveryType = t
                messageData = UpdateProgressPrtMsg (p.toProgressUpdateInfo())
            }.getMessageInfo()
            |> sendMessage data.messagingDataVersion data.workerNodeId.messagingClientId

        let r1 =
            match r11 with
            | Ok v -> Ok v
            | Error e -> OnUpdateProgressErr (UnableToSendProgressMsgErr (p.runQueueId, e)) |> Error

        let result =
            if completed
            then
                let r2 = deleteRunQueue p.runQueueId
                foldUnitResults DistributedProcessingError.addError [ r0; r1; r2 ]
            else foldUnitResults DistributedProcessingError.addError [ r0; r1 ]

        Logger.logTrace (fun () -> $"    onUpdateProgress: runQueueId = %A{p.runQueueId}, t = %A{t}, result = %A{result}.")
        result


    /// TODO kk:20240928 - Add error handling.
    let private createSystemProxy<'P, 'X, 'C> (data : RunnerData) =
        let updater = AsyncUpdater<ResultInitData, ResultSliceData<'C>, ResultData<'C>>(ResultDataUpdater<'C>(), ()) :> IAsyncUpdater<ResultSliceData<'C>, ResultData<'C>>

        let proxy : SolverRunnerSystemProxy<'P, 'X, 'C> =
            {
                callBackProxy =
                    {
                        progressCallBack = ProgressCallBack (fun cb pd -> onUpdateProgress<'P> data cb pd |> ignore)
                        resultCallBack = ResultCallBack (fun c -> onSaveResults<'P> data c |> ignore)
                        checkCancellation = CheckCancellation (fun q -> tryCheckCancellation q |> toOption |> Option.bind id)
                    }
                addResultData = updater.addContent
                getResultData = updater.getContent
                checkNotification = fun q -> tryCheckNotification q |> toOption |> Option.bind id
                clearNotification = fun q -> tryClearNotification q |> toOption |> Option.defaultValue ()
            }

        proxy


    type WolframParams
        with

        member w.inputLocationInto s =
            {
                locationInfo =
                    {
                        location = w.wolframInputFolder
                        solverName = s
                    }
                optionalFolder = None
            }

        member w.outputLocationInto s =
            {
                locationInfo =
                    {
                        location = w.wolframOutputFolder
                        solverName = s
                    }
                optionalFolder = None
            }


    let getSolverWolframParams (s : SolverId) =
        let w = loadWolframParams()

        match tryGetSolverName s with
        | Ok (Some s) ->
            match getFolderLocation (w.inputLocationInto s), getFolderLocation (w.outputLocationInto s) with
            | Ok i, Ok o ->
                { w with wolframInputFolder = i; wolframOutputFolder = o }
            | Error e1, Ok o ->
                Logger.logError $"Input folder error: '%A{e1}', output folder: '%A{o}'."
                w
            | Ok i, Error e2 ->
                Logger.logError $"Input folder: '%A{i}', output folder error: '%A{e2}'."
                w
            | Error e1, Error e2 ->
                Logger.logError $"Input folder error: '%A{e1}', output folder error: '%A{e2}'."
                w
        | Ok None ->
            Logger.logError $"Unable to get solver name for '%A{s}'."
            w
        | Error e ->
            Logger.logError $"Error gettint solver name for '%A{s}', e: '%A{e}'."
            w


    let tryGetWolframFileNames solverId (fullName : string) (FileSuffix fileSuffix) =
        let w = getSolverWolframParams solverId

        let i = (FileName $"{fullName}__{fileSuffix}.m").tryGetFullFileName (Some w.wolframInputFolder)
        let o = (FileName $"{fullName}__{fileSuffix}.png").tryGetFullFileName (Some w.wolframOutputFolder)

        (i, o)


    let getWolframChart solverId d fullName c fileSuffix getData =
        match tryGetWolframFileNames solverId fullName fileSuffix with
        | Ok i, Ok o ->
            let c1 = c |> List.rev
            let t = c1 |> List.map(fun e -> double e.t)
            getData d c1 t |> Option.map (getListLinePlot i o ListLineParams.defaultValue)
        | _ ->
            Logger.logError $"getWolframChart - Cannot get data for: %A{fullName}."
            None


    type SolverRunnerSystemProxy<'P, 'X, 'C>
        with
        static member create (data : RunnerData) = createSystemProxy<'P, 'X, 'C> data


    type SolverRunnerSystemContext<'D, 'P, 'X, 'C>
        with
        static member create () =
            {
                logCrit = saveSolverRunnerErrFs solverProgramName
                workerNodeServiceInfo = loadWorkerNodeServiceInfo messagingDataVersion
                tryLoadRunQueue = tryLoadRunQueue<'D>
                getAllowedSolvers = getAllowedSolvers
                checkRunning = fun no q -> checkRunning (RunQueueRunning (no, q))
                tryStartRunQueue = tryStartRunQueue
                createSystemProxy = createSystemProxy
                runSolver = runSolver
            }


    type SolverRunnerData
        with
        static member create solverId runQueueId forceRun =
            {
                runQueueId = runQueueId
                solverId = solverId
                forceRun = forceRun
                cancellationCheckFreq = TimeSpan.FromMinutes 5.0
                messagingDataVersion = messagingDataVersion
            }


    let runSolverProcess<'D, 'P, 'X, 'C> (ctx: SolverRunnerSystemContext<'D, 'P, 'X, 'C>) getUserProxy (data : SolverRunnerData) =
        let logCrit = ctx.logCrit
        let q = data.runQueueId
        let i = ctx.workerNodeServiceInfo

        let runnerDataCrit =
            {
                runQueueId = q
                partitionerId = i.workerNodeInfo.partitionerId
                workerNodeId = i.workerNodeInfo.workerNodeId
                messagingDataVersion = data.messagingDataVersion
                started = DateTime.Now
                cancellationCheckFreq = data.cancellationCheckFreq
                optionalFolder = None
            }

        // let logCritResult = logCrit (SolverRunnerCriticalError.create q "runSolver: Starting solver.")
        // Logger.logInfo $"runSolver: Starting solver with runQueueId: {q}, logCritResult = %A{logCritResult}."
        Logger.logInfo $"runSolver: Starting solver with runQueueId: {q}."

        let exitWithLogWarn e x =
            Logger.logWarn $"runSolver: ERROR: %A{e}, exit code: %A{x}."
            x

        let abortWithLogCrit e x =
            let cb = Some $"%A{e}, %A{x}" |> AbortCalculation |> CancelledCalculation |> FinalCallBack
            let pd = getFailedProgressData e
            let r = onUpdateProgress<'P> runnerDataCrit cb pd
            Logger.logCrit $"runSolver: ERROR: %A{e}, exit code: %A{x}, r: %A{r}."
            SolverRunnerCriticalError.create q e |> logCrit |> ignore
            x

        match ctx.tryLoadRunQueue q with
        | Ok (w, st) ->
            let allowedSolvers =
                match data.forceRun with
                | false -> ctx.getAllowedSolvers i.workerNodeInfo |> Some
                | true -> None

            match w.solverId = data.solverId with
            | true ->
                match ctx.checkRunning allowedSolvers q with
                | CanRun ->
                    match st with
                    | NotStartedRunQueue | InProgressRunQueue ->
                        match ctx.tryStartRunQueue q with
                        | Ok() ->
                            Logger.logInfo $"runSolver: Starting solver with runQueueId: {q}."
                            let userProxy = getUserProxy w.modelData
                            let runnerData = { runnerDataCrit with optionalFolder = userProxy.solverProxy.getOptionalFolder q w }

                            let rd : RunnerData<'D> =
                                {
                                    runnerData = runnerData
                                    modelData = w
                                }

                            let proxy = ctx.createSystemProxy runnerData

                            let ctxr =
                                {
                                    runnerData = rd
                                    systemProxy = proxy
                                    userProxy = userProxy
                                }

                            // The call below does not return until the run is completed OR cancelled in some way.
                            ctx.runSolver ctxr
                            Logger.logInfo $"runSolver: Call to solver.run() with runQueueId: {q} completed."
                            CompletedSuccessfully
                        | Error e ->
                            Logger.logError $"runSolver: ERROR: {e}."
                            abortWithLogCrit e UnknownException
                    | CancelRequestedRunQueue ->
                        // If we got here that means that the solver was terminated before it had a chance to process cancellation.
                        // At this point we have no choice but abort the calculation because there is no data available to continue.
                        let errMessage = "The solver was terminated before processing cancellation. Aborting."
                        abortWithLogCrit errMessage NotProcessedCancellation
                    | _ -> abortWithLogCrit $"Invalid run queue status: %A{st}" InvalidRunQueueStatus
                | AlreadyRunning p -> exitWithLogWarn (AlreadyRunning p) SolverAlreadyRunning
                | TooManyRunning n -> exitWithLogWarn (TooManyRunning n) TooManySolversRunning
                | GetProcessesByNameExn e -> abortWithLogCrit e CriticalError
            | false -> abortWithLogCrit $"Invalid solverId: {w.solverId}, expected: {data.solverId}" InvalidSolverId
        | Error e -> abortWithLogCrit e DatabaseErrorOccurred
