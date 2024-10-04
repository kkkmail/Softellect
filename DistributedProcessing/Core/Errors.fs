namespace Softellect.DistributedProcessing

open System
open System.Threading

open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Rop
open Softellect.Sys.TimerEvents

open Softellect.Wcf.Common
open Softellect.Wcf.Client

open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy
open Softellect.Wcf.Errors
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.DistributedProcessing.Primitives.Common

module Errors =

    type InvalidRunQueueData =
        {
            runQueueId : RunQueueId
            runQueueStatusFrom : RunQueueStatus option
            runQueueStatusTo : RunQueueStatus
            workerNodeIdOptFrom : WorkerNodeId option
            workerNodeIdOptTo : WorkerNodeId option
        }


    type TryLoadSolverRunnersError =
        | TryLoadSolverRunnersDbErr of DbError


    type TryGetRunningSolversCountError =
        | TryGetRunningSolversCountDbErr of DbError


    type TryPickRunQueueError =
        | TryPickRunQueueDbErr of DbError


    type LoadAllActiveRunQueueIdError =
        | LoadAllActiveRunQueueIdDbErr of DbError


    type TryStartRunQueueError =
        | TryStartRunQueueDbErr of DbError
        | CannotStartRunQueue of RunQueueId


    type TryCompleteRunQueueError =
        | TryCompleteRunQueueDbErr of DbError
        | CannotCompleteRunQueue of RunQueueId


    type TryCancelRunQueueError =
        | TryCancelRunQueueDbErr of DbError
        | CannotCancelRunQueue of RunQueueId


    type TryFailRunQueueError =
        | TryFailRunQueueDbErr of DbError
        | CannotFailRunQueue of RunQueueId


    type TryRequestCancelRunQueueError =
        | TryRequestCancelRunQueueDbErr of DbError
        | CannotRequestCancelRunQueue of RunQueueId


    type TryNotifyRunQueueError =
        | TryNotifyRunQueueDbErr of DbError
        | CannotNotifyRunQueue of RunQueueId


    type TryCheckCancellationError =
        | TryCheckCancellationDbErr of DbError


    type TryCheckNotificationError =
        | TryCheckNotificationDbErr of DbError
        | CannotCheckNotification of RunQueueId


    type TryClearNotificationError =
        | TryClearNotificationDbErr of DbError
        | CannotClearNotification of RunQueueId


    type TryUpdateProgressError =
        | TryUpdateProgressDbErr of DbError
        | CannotUpdateProgress of RunQueueId


    type SendMessageError =
        | MessagingErr of MessagingError


    type MapRunQueueError =
        | MapRunQueueDbErr of DbError
        | CannotMapRunQueue of RunQueueId


    type LoadRunQueueError =
        | LoadRunQueueDbErr of DbError


    type LoadRunQueueProgressError =
        | LoadRunQueueProgressDbErr of DbError


    type TryLoadFirstRunQueueError =
        | TryLoadFirstRunQueueDbErr of DbError


    type TryLoadRunQueueError =
        | TryLoadRunQueueDbErr of DbError
        | SerializationErr of SerializationError
        | InvalidRunQueueStatus of RunQueueId * int
        | ExnWhenTryLoadRunQueue of RunQueueId * exn
        | UnableToFindRunQueue of RunQueueId


    type TryResetRunQueueError =
        | TryResetRunQueueDbErr of DbError
        | ResetRunQueueEntryErr of RunQueueId


    type SaveRunQueueError =
        | SaveRunQueueDbErr of DbError


    type DeleteRunQueueError =
        | DeleteRunQueueEntryErr of RunQueueId
        | DeleteRunQueueDbErr of DbError


    type TryUpdateRunQueueRowError =
        | InvalidStatusTransitionErr of InvalidRunQueueData
        | InvalidDataErr of InvalidRunQueueData
        | TryUpdateRunQueueRowDbErr of DbError


    type UpsertRunQueueError =
        | UpsertRunQueueDbErr of DbError


    type LoadWorkerNodeInfoError =
        | LoadWorkerNodeInfoDbErr of DbError
        | UnableToLoadWorkerNodeErr of WorkerNodeId


    type UpsertWorkerNodeInfoError =
        | UpsertWorkerNodeInfoDbErr of DbError


    type UpsertWorkerNodeErrError =
        | UpsertWorkerNodeErrDbErr of DbError


    type TryGetAvailableWorkerNodeError =
        | TryGetAvailableWorkerNodeDbErr of DbError


    type TryGetSolverNameError =
        | TryGetSolverNameDbErr of DbError


    type TryRunSolverProcessError =
        | FailedToRunSolverProcessErr of RunQueueId
        | FailedToRunSolverProcessWithExErr of (RunQueueId * exn)
        | CannotRunSolverProcessErr of RunQueueId
        | CannotLoadSolverNameErr of RunQueueId
        | FailedToLoadSolverNameErr of RunQueueId


    type OnRunModelError =
        | CannotRunModelErr of RunQueueId
        | CannotDeleteRunQueueErr of RunQueueId


    type OnProcessMessageError =
        | CannotSaveModelDataErr of MessageId * RunQueueId
//        | OnRunModelFailedErr of MessageId * RunQueueId
//        | ModelAlreadyRunningErr of MessageId * RunQueueId
        | InvalidMessageErr of (MessageId * string)
        | FailedToCancelErr of (MessageId * RunQueueId * exn)


//    type OnRequestResultError =
//        | CannotFindRunQueueErr of RunQueueId


//    //type WrkSettingsError =
//    //    | InvalidSettings of string
//    //    | WrkSettingExn of exn


//    type WorkerNodeError =
////        | OnRunModelErr of OnRunModelError
////        | OnProcessMessageErr of OnProcessMessageError
//        | OnGetMessagesErr of OnGetMessagesError
//        | OnRequestResultErr of OnRequestResultError
//        //| WrkSettingsErr of WrkSettingsError


    type WorkerNodeWcfError =
        | ConfigureWcfErr of WcfError
        | MonitorWcfErr of WcfError
        | PingWcfErr of WcfError


//    type WorkerNodeServiceError =
////        | WorkerNodeWcfErr of WorkerNodeWcfError
////        | UnableToStartMessagingClientErr of MessagingError
////        | UnableToCreateWorkerNodeServiceErr
//        | ServiceUnavailableErr
//        | UpdateLocalProgressErr of string
//        | ConfigureServiceErr of string
//        | MonitorServiceErr of string

//        // ======
//        // Ugly stuff
//        // TODO kk:20240722 - WorkNode error types are now inconsistent and conflict with messaging error types.
//        // See: https://github.com/kkkmail/CoreClm/issues/40
//        | UnableToRegisterWorkerNodeErr of MessagingError


// Model runner errors
    type RunModelRunnerError =
        | MessagingRunnerErr of MessagingError
        | MissingWorkerNodeRunnerErr of RunQueueId
        | UnableToLoadModelDataRunnerErr of RunQueueId // * ModelDataId


    type TryRunFirstModelRunnerError =
        | TryLoadFirstRunQueueRunnerErr
        | TryGetAvailableWorkerNodelRunnerErr
        | UpsertRunQueueRunnerErr
        | UnableToRunModelRunnerErr
        | UnableToRunModelAndUpsertStatusRunnerErr
        | UnableToGetWorkerNodeRunnerErr


    type TryCancelRunQueueRunnerError =
        | TryLoadRunQueueRunnerErr of RunQueueId
        | InvalidRunQueueStatusRunnerErr of RunQueueId
        | MessagingTryCancelRunQueueRunnerErr of MessagingError


    type TryRequestResultsRunnerError =
        | TryLoadRunQueueRunnerErr of RunQueueId
        | MessagingTryRequestResultsRunnerErr of MessagingError

    type TryRunAllModelsRunnerError =
        | UnableToTryRunFirstModelRunnerErr


    type UpdateProgressRunnerError =
        | UnableToLoadRunQueueRunnerErr of RunQueueId
        | UnableToFindLoadRunQueueRunnerErr of RunQueueId
        | InvalidRunQueueStatusRunnerErr of RunQueueId
        | CompletelyInvalidRunQueueStatusRunnerErr of RunQueueId // This should never happen but we still have to account for it. It if does, then we are in a BIG trouble.


    type RegisterRunnerError =
        | UnableToUpsertWorkerNodeInfoRunnerErr of WorkerNodeId


    type UnregisterRunnerError =
        | UnableToLoadWorkerNodeInfoRunnerErr of WorkerNodeId
        | UnableToUpsertWorkerNodeInfoOnUnregisterRunnerErr of WorkerNodeId


    type SaveResultRunnerError =
        | UnableToSaveResultDataRunnerErr of RunQueueId


    type SaveChartsRunnerError =
        | UnableToSaveChartsRunnerErr of RunQueueId


    type ProcessMessageRunnerError =
        | ErrorWhenProcessingMessageRunnerErr of MessageId
        | InvalidMessageTypeRunnerErr of MessageId
        | OnGetMessagesRunnerErr of OnGetMessagesError


    type TryGetAvailableWorkerNodeRunnerError =
        | A

    // ==================================
    // Solver Runner errors

    type OnSaveChartsError =
        | SendChartMessageErr of (MessagingClientId * RunQueueId * MessagingError)


    type OnUpdateProgressError =
        | UnableToSendProgressMsgErr of RunQueueId
        | UnableToFindMappingErr of RunQueueId



    //type ModelRunnerError =
    //    | RunModelRunnerErr of RunModelRunnerError
    //    | TryRunFirstModelRunnerErr of TryRunFirstModelRunnerError
    //    | TryCancelRunQueueRunnerErr of TryCancelRunQueueRunnerError
    //    | TryRequestResultsRunnerErr of TryRequestResultsRunnerError
    //    | TryRunAllModelsRunnerErr of TryRunAllModelsRunnerError
    //    | UpdateProgressRunnerErr of UpdateProgressRunnerError
    //    | RegisterRunnerErr of RegisterRunnerError
    //    | UnregisterRunnerErr of UnregisterRunnerError
    //    | SaveResultRunnerErr of SaveResultRunnerError
    //    | SaveChartsRunnerErr of SaveChartsRunnerError
    //    | ProcessMessageRunnerErr of ProcessMessageRunnerError
    //    | TryGetAvailableWorkerNodeRunnerErr of TryGetAvailableWorkerNodeRunnerError


    type DistributedProcessingError =
        | DistributedProcessingAggregateErr of DistributedProcessingError * List<DistributedProcessingError>
        | TryLoadSolverRunnersErr of TryLoadSolverRunnersError
        | TryGetRunningSolversCountErr of TryGetRunningSolversCountError
        | TryPickRunQueueErr of TryPickRunQueueError
        | LoadAllActiveRunQueueIdErr of LoadAllActiveRunQueueIdError
        | TryStartRunQueueErr of TryStartRunQueueError
        | TryCompleteRunQueueErr of TryCompleteRunQueueError
        | TryCancelRunQueueErr of TryCancelRunQueueError
        | TryFailRunQueueErr of TryFailRunQueueError
        | TryRequestCancelRunQueueErr of TryRequestCancelRunQueueError
        | TryNotifyRunQueueErr of TryNotifyRunQueueError
        | TryCheckCancellationErr of TryCheckCancellationError
        | TryCheckNotificationErr of TryCheckNotificationError
        | TryClearNotificationErr of TryClearNotificationError
        | TryUpdateProgressErr of TryUpdateProgressError

        | MapRunQueueErr of MapRunQueueError
        | LoadRunQueueErr of LoadRunQueueError
        | LoadRunQueueProgressErr of LoadRunQueueProgressError
        | TryLoadFirstRunQueueErr of TryLoadFirstRunQueueError
        | TryLoadRunQueueErr of TryLoadRunQueueError

        | TryResetRunQueueErr of TryResetRunQueueError
        | SaveRunQueueErr of SaveRunQueueError
        | DeleteRunQueueErr of DeleteRunQueueError
        | TryUpdateRunQueueRowErr of TryUpdateRunQueueRowError
        | UpsertRunQueueErr of UpsertRunQueueError
        | TimerEventErr of TimerEventError
        | LoadWorkerNodeInfoErr of LoadWorkerNodeInfoError
        | UpsertWorkerNodeInfoErr of UpsertWorkerNodeInfoError
        | UpsertWorkerNodeErrErr of UpsertWorkerNodeErrError
        | TryGetAvailableWorkerNodeErr of TryGetAvailableWorkerNodeError

        | OnProcessMessageErr of OnProcessMessageError
        | UnableToRegisterWorkerNodeErr of MessagingError
        | CreateServiceImplWorkerNodeErr of MessagingError

        | OnRunModelErr of OnRunModelError
        | UnableToStartMessagingClientErr of MessagingError
        | UnableToCreateWorkerNodeServiceErr

        // Model runner errors
        | RunModelRunnerErr of RunModelRunnerError
        | TryRunFirstModelRunnerErr of TryRunFirstModelRunnerError
        | TryCancelRunQueueRunnerErr of TryCancelRunQueueRunnerError
        | TryRequestResultsRunnerErr of TryRequestResultsRunnerError
        | TryRunAllModelsRunnerErr of TryRunAllModelsRunnerError
        | UpdateProgressRunnerErr of UpdateProgressRunnerError
        | RegisterRunnerErr of RegisterRunnerError
        | UnregisterRunnerErr of UnregisterRunnerError
        | SaveResultRunnerErr of SaveResultRunnerError
        | SaveChartsRunnerErr of SaveChartsRunnerError
        | ProcessMessageRunnerErr of ProcessMessageRunnerError
        | TryGetAvailableWorkerNodeRunnerErr of TryGetAvailableWorkerNodeRunnerError

        | WorkerNodeWcfErr of WorkerNodeWcfError
        | TryGetSolverNameErr of TryGetSolverNameError

        // Solver runner errors
        | OnSaveChartsErr of OnSaveChartsError
        | OnUpdateProgressErr of OnUpdateProgressError
        | TryRunSolverProcessErr of TryRunSolverProcessError

        static member addError a b =
            match a, b with
            | DistributedProcessingAggregateErr (x, w), DistributedProcessingAggregateErr (y, z) -> DistributedProcessingAggregateErr (x, w @ (y :: z))
            | DistributedProcessingAggregateErr (x, w), _ -> DistributedProcessingAggregateErr (x, w @ [b])
            | _, DistributedProcessingAggregateErr (y, z) -> DistributedProcessingAggregateErr (a, y :: z)
            | _ -> DistributedProcessingAggregateErr (a, [b])

        static member (+) (a, b) = DistributedProcessingError.addError a b
        member a.add b = a + b

    // ==================================
    // Partitioner errors


    // ==================================

    /// We need a special error type to handle critical errors that can occur during the solver run.
    /// Unique errorId is needed because we save critical errors into file system (assuming that nothing else is available).
    ///
    /// Critical error is NOT a part of DistributedProcessingError by design.
    type SolverRunnerCriticalError =
        {
            errorId : ErrorId
            runQueueId : RunQueueId
            errorMessage : string
        }

        static member create q e =
            {
                errorId = ErrorId.getNewId()
                runQueueId = q
                errorMessage = $"%A{e}"
            }

    // ==================================

    type DistributedProcessingUnitResult = UnitResult<DistributedProcessingError>
    type DistributedProcessingResult<'T> = Result<'T, DistributedProcessingError>
    type DistributedProcessingListResult<'T> = ListResult<'T, DistributedProcessingError>
    type DistributedProcessingStateWithResult<'T> = StateWithResult<'T, DistributedProcessingError>


    /// See: https://stackoverflow.com/questions/49974736/how-to-declare-a-generic-exception-types-in-f
    /// We have to resort to throwing a specific exception in order
    /// to perform early termination from deep inside C# ODE solver.
    /// There seems to be no other easy and clean way. Revisit if that changes.
    //type ComputationAbortedException<'P> (pd : ProgressData<'P>, ct : CancellationType) =
    //    inherit System.Exception ()

    //    member _.progressData = pd
    //    member _.cancellationType = ct

    //type ComputationAbortedException (pd : ProgressData, ct : CancellationType) =
    //    inherit System.Exception ()

    //    member _.progressData = pd
    //    member _.cancellationType = ct
