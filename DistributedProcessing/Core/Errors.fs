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
open Softellect.DistributedProcessing.Primitives

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



//    type OnRunModelError =
//        | CannotRunModelErr of RunQueueId
//        | CannotDeleteRunQueueErr of RunQueueId


//    type OnProcessMessageError =
//        | CannotSaveModelDataErr of MessageId * RunQueueId
////        | OnRunModelFailedErr of MessageId * RunQueueId
////        | ModelAlreadyRunningErr of MessageId * RunQueueId
//        | InvalidMessageErr of (MessageId * string)
//        | FailedToCancelErr of (MessageId * RunQueueId * exn)


//    type OnRequestResultError =
//        | CannotFindRunQueueErr of RunQueueId


//    //type WrkSettingsError =
//    //    | InvalidSettings of string
//    //    | WrkSettingExn of exn


//    type WorkerNodeError =
//        | OnRunModelErr of OnRunModelError
//        | OnProcessMessageErr of OnProcessMessageError
//        | OnGetMessagesErr of OnGetMessagesError
//        | OnRequestResultErr of OnRequestResultError
//        //| WrkSettingsErr of WrkSettingsError


//    type WorkerNodeWcfError =
//        | ConfigureWcfErr of WcfError
//        | MonitorWcfErr of WcfError
//        | PingWcfErr of WcfError


//    type WorkerNodeServiceError =
//        | WorkerNodeWcfErr of WorkerNodeWcfError
//        | UnableToStartMessagingClientErr of MessagingError
//        | UnableToCreateWorkerNodeServiceErr
//        | ServiceUnavailableErr
//        | UpdateLocalProgressErr of string
//        | ConfigureServiceErr of string
//        | MonitorServiceErr of string

//        // ======
//        // Ugly stuff
//        // TODO kk:20240722 - WorkNode error types are now inconsistent and conflict with messaging error types.
//        // See: https://github.com/kkkmail/CoreClm/issues/40
//        | UnableToREgisterWorkerNodeErr of MessagingError
//        | CreateServiceImplWorkerNodeErr of MessagingError


    type DistributedProcessingError =
        | WorkerNodeAggregateErr of DistributedProcessingError * List<DistributedProcessingError>
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

        static member private addError a b =
            match a, b with
            | WorkerNodeAggregateErr (x, w), WorkerNodeAggregateErr (y, z) -> WorkerNodeAggregateErr (x, w @ (y :: z))
            | WorkerNodeAggregateErr (x, w), _ -> WorkerNodeAggregateErr (x, w @ [b])
            | _, WorkerNodeAggregateErr (y, z) -> WorkerNodeAggregateErr (a, y :: z)
            | _ -> WorkerNodeAggregateErr (a, [b])

        static member (+) (a, b) = DistributedProcessingError.addError a b
        member a.add b = a + b


    // ==================================
    // Partitioner errors


