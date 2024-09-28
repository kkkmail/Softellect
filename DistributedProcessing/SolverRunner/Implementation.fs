namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.Errors

open Argu
open Softellect.Sys.Rop
//open ClmSys.ClmErrors
//open ClmSys.GeneralErrors
//open ClmSys.ExitErrorCodes
//open ClmSys.GeneralPrimitives
//open ClmSys.SolverRunnerPrimitives
//open Primitives.GeneralPrimitives
//open Primitives.SolverPrimitives
//open ServiceProxy.SolverProcessProxy
//open SolverRunner.SolverRunnerCommandLine
//open NoSql.FileSystemTypes
//open DbData.Configuration
//open DbData.WorkerNodeDatabaseTypes
open Softellect.Sys
open Softellect.Messaging.Primitives
open Softellect.Messaging.Client
//open ClmSys.WorkerNodeData
//open ContGenServiceInfo.ServiceInfo
//open WorkerNodeServiceInfo.ServiceInfo
//open MessagingServiceInfo.ServiceInfo
//open ClmSys.ContGenPrimitives
//open ClmSys.WorkerNodePrimitives
//open ServiceProxy.SolverRunner
//open SolverRunner.SolverRunnerTasks
//open DbData.MsgSvcDatabaseTypes
open System.Diagnostics
//open Primitives.VersionInfo
//open Primitives.SolverRunnerErrors
open Softellect.Messaging.DataAccess
open Softellect.Sys.ExitErrorCodes
open Softellect.DistributedProcessing.Proxy.SolverRunner
open Softellect.DistributedProcessing.DataAccess.SolverRunner
open Softellect.DistributedProcessing.SolverRunner.CommandLine
open Softellect.DistributedProcessing.SolverRunner.NoSql
open Softellect.DistributedProcessing.SolverRunner.Runner
open Softellect.Sys.Core


module Implementation =

    let private name = "SolverRunner"

    /// Extra solver's "overhead" allowed when running SolverRunner by hands.
    /// This is needed when two versions share the same machine and one version has some stuck
    /// work, which needs to be started.
    ///
    /// WorkNodeService does not use that overhead and so it will not start more than allowed.
    [<Literal>]
    let AllowedOverhead = 0.20


    //let private toError g f = f |> g |> SolverRunnerErr |> Error
    //let private addError g f e = ((f |> g |> SolverRunnerErr) + e) |> Error

    let runSolver solverProxy w = failwith "runSolver is not implemented yet"


    let onSaveCharts (proxy : SendMessageProxy<'D, 'P>) (r : ChartGenerationResult) =
        match r with
        | GeneratedCharts c ->
            printfn $"onSaveCharts: Sending charts with runQueueId = %A{c.runQueueId}."

            let result =
                {
                    partitionerRecipient = proxy.partitionerId
                    deliveryType = GuaranteedDelivery
                    messageData = c |> SaveChartsPrtMsg
                }.getMessageInfo()
                |> proxy.sendMessage

            match result with
            | Ok v -> Ok v
            | Error e -> OnSaveChartsErr (SendChartMessageErr (proxy.partitionerId.messagingClientId, c.runQueueId, e)) |> Error
            //|> Rop.bindError (addError OnSaveChartsErr (SendChartMessageErr (proxy.partitionerId.messagingClientId, c.runQueueId)))
        | NotGeneratedCharts ->
            printfn "onSaveCharts: No charts."
            Ok()


    let toDeliveryType (p : ProgressUpdateInfo<'P>) =
        match p.updatedRunQueueStatus with
        | Some s ->
            match s with
            | NotStartedRunQueue -> (GuaranteedDelivery, false)
            | InactiveRunQueue -> (GuaranteedDelivery, false)
            | RunRequestedRunQueue -> (GuaranteedDelivery, false)
            | InProgressRunQueue -> (GuaranteedDelivery, false)
            | CompletedRunQueue -> (GuaranteedDelivery, true)
            | FailedRunQueue -> (GuaranteedDelivery, true)
            | CancelRequestedRunQueue -> (GuaranteedDelivery, false)
            | CancelledRunQueue -> (GuaranteedDelivery, true)

        | None -> (NonGuaranteedDelivery, false)


    let onUpdateProgress (proxy : OnUpdateProgressProxy<'D, 'P>) (p : ProgressUpdateInfo<'P>) : DistributedProcessingUnitResult =
        printfn $"onUpdateProgress: runQueueId = %A{p.runQueueId}, progress = %A{p.progressData}."
        let t, completed = toDeliveryType p
        let r0 = proxy.tryUpdateProgressData p.progressData

        let r11 =
            {
                partitionerRecipient = proxy.sendMessageProxy.partitionerId
                deliveryType = t
                messageData = UpdateProgressPrtMsg p
            }.getMessageInfo()
            |> proxy.sendMessageProxy.sendMessage

        let r1 =
            match r11 with
            | Ok v -> Ok v
            | Error e -> OnUpdateProgressErr (UnableToSendProgressMsgErr p.runQueueId) |> Error
            //|> Rop.bindError (addError OnUpdateProgressErr (UnableToSendProgressMsgErr p.runQueueId))

        let result =
            if completed
            then
                let r2 = proxy.tryDeleteRunQueue()
//                let r2 = Ok()
                foldUnitResults DistributedProcessingError.addError [ r0; r1; r2 ]
            else foldUnitResults DistributedProcessingError.addError [ r0; r1 ]

        printfn $"    onUpdateProgress: runQueueId = %A{p.runQueueId}, result = %A{result}."
        result


    /// TODO kk:20240928 - Add error handling.
    let createSystemProxy<'D, 'P, 'X, 'C> (data : RunnerData<'D>) =
        let updater = AsyncUpdater<ChartInitData, ChartSliceData<'C>, ChartData<'C>>(ChartDataUpdater<'C>(), ()) :> IAsyncUpdater<ChartSliceData<'C>, ChartData<'C>>

        let proxy : SystemProxy<'D, 'P, 'X, 'C> =
            {
                callBackProxy =
                    {
                        progressCallBack = 0
                        chartCallBack = 0
                        checkCancellation = CheckCancellation (fun q -> tryCheckCancellation q |> toOption |> Option.bind id)
                    }
                addChartData = updater.addContent
                getChartData = updater.getContent
                checkNotification = fun q -> tryCheckNotification q |> toOption |> Option.bind id
                clearNotification = fun q -> tryClearNotification q |> toOption |> Option.defaultValue ()
            }

        proxy


    //let private tryLoadWorkerNodeSettings () = tryLoadWorkerNodeSettings None None
    //let private name = WorkerNodeServiceName.netTcpServiceName.value.value |> MessagingClientName


    //type SolverRunnerProxy<'P>
    //    with
    //    static member create logCrit (proxy : OnUpdateProgressProxy<'D, 'P>) =
    //        let checkCancellation q = tryCheckCancellation q |> Rop.toOption |> Option.bind id
    //        let checkNotification q = tryCheckNotification q |> Rop.toOption |> Option.bind id
    //        let clearNotification q = tryClearNotification q

    //        {
    //            solverUpdateProxy =
    //                {
    //                    updateProgress = onUpdateProgress proxy
    //                    checkCancellation = checkCancellation
    //                    logCrit = logCrit
    //                }

    //            solverNotificationProxy =
    //                {
    //                    checkNotificationRequest = checkNotification
    //                    clearNotificationRequest = clearNotification
    //                }

    //            saveCharts = onSaveCharts proxy.sendMessageProxy
    //            logCrit = logCrit
    //        }


    // Send the message directly to local database.
    let private sendMessage<'D, 'P> messagingDataVersion m i =
        createMessage messagingDataVersion m i
        |> saveMessage<DistributedProcessingMessageData<'D, 'P>> messagingDataVersion
        //|> bindError (fun e -> MessagingErr e |> SendMessageErr |> Error)


    let private tryStartRunQueue q =
        let pid = Process.GetCurrentProcess().Id |> ProcessId
        tryStartRunQueue q pid


    let getAllowedSolvers i =
        let noOfCores = i.noOfCores
        max ((float noOfCores) * (1.0 + AllowedOverhead) |> int) (noOfCores + 1)


    let runSolverProcessImpl<'D, 'P> messagingDataVersion (results : ParseResults<SolverRunnerArguments>) usage : int =
        //let c = getWorkerNodeSvcConnectionString
        let logCrit = saveSolverRunnerErrFs name

        match results.TryGetResult RunQueue |> Option.bind (fun e -> e |> RunQueueId |> Some) with
        | Some q ->
            let exitWithLogCrit e x =
                printfn $"runSolver: ERROR: {e}, exit code: {x}."
                SolverRunnerCriticalError.create q e |> logCrit |> ignore
                x

            let svc : WorkerNodeServiceInfo option = failwith "loadWorkerNodeServiceInfo messagingDataVersion" // 

            match tryLoadRunQueue<'D> q, svc with
            | Ok (w, st), Some s ->
                let allowedSolvers =
                    match results.TryGetResult ForceRun |> Option.defaultValue false with
                    | false -> getAllowedSolvers s.workerNodeInfo |> Some
                    | true -> None

                match checkRunning allowedSolvers q with
                | CanRun ->
                    let proxy : OnUpdateProgressProxy<'D, 'P> =
                        {
                            tryDeleteRunQueue = fun () -> deleteRunQueue q
                            tryUpdateProgressData = tryUpdateProgress<'P> q

                            sendMessageProxy =
                                {
                                    partitionerId = s.workerNodeInfo.partitionerId
                                    sendMessage = sendMessage<'D, 'P> messagingDataVersion s.workerNodeInfo.workerNodeId.messagingClientId
                                }
                        }

                    //let solverProxy = SolverRunnerProxy.create logCrit proxy

                    match st with
                    | NotStartedRunQueue | InProgressRunQueue ->
                        match tryStartRunQueue q with
                        | Ok() ->
                            printfn $"runSolver: Starting solver with runQueueId: {q}."
                            // The call below does not return until the run is completed OR cancelled in some way.
                            runSolver solverProxy w
                            printfn "runSolver: Call to solver.run() completed."
                            CompletedSuccessfully
                        | Error e -> exitWithLogCrit e UnknownException
                    | CancelRequestedRunQueue ->
                        // If we got here that means that the solver was terminated before it had a chance to process cancellation.
                        // At this point we have no choice but abort the calculation because there is no data available to continue.
                        let errMessage = "The solver was terminated before processing cancellation. Aborting."
                        //let p0 = ProgressData.defaultValue
                        //let p = { p0 with progressData = { p0.progressData with errorMessageOpt = errMessage |> ErrorMessage |> Some } }
                        //getProgress w (Some FailedRunQueue) p |> (updateFinalProgress solverProxy q errMessage)
                        exitWithLogCrit errMessage NotProcessedCancellation
                    | _ -> exitWithLogCrit ($"Invalid run queue status: {st}") InvalidRunQueueStatus
                | AlreadyRunning p -> exitWithLogCrit (AlreadyRunning p) SolverAlreadyRunning
                | TooManyRunning n -> exitWithLogCrit (TooManyRunning n) TooManySolversRunning
                | GetProcessesByNameExn e -> exitWithLogCrit e CriticalError
            | Error e, _ -> exitWithLogCrit e DatabaseErrorOccurred
            | _, None -> exitWithLogCrit "Unable to load WorkerNodeSettings." CriticalError
        | None ->
            printfn $"runSolver: {usage}."
            InvalidCommandLineArgs
