#nowarn "1104"
namespace Softellect.DistributedProcessing.DataAccess

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
open System
open FSharp.Data.Sql
open Softellect.Sys.Core
open Softellect.Sys.Primitives
open Softellect.Sys.Retry
open Softellect.Sys.DataAccess
open Softellect.DistributedProcessing.VersionInfo
open Softellect.Sys.AppSettings
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.Primitives.Common

// ==========================================
// Blank #if template blocks

#if PARTITIONER
#endif

#if SOLVER_RUNNER || WORKER_NODE
#endif

#if SOLVER_RUNNER
#endif

#if WORKER_NODE
#endif

#if PARTITIONER || SOLVER_RUNNER || WORKER_NODE
#endif

// ==========================================
// Open declarations

#if PARTITIONER
#endif

#if SOLVER_RUNNER
open Softellect.DistributedProcessing.Primitives.SolverRunner
open Softellect.DistributedProcessing.SolverRunner.Primitives
#endif

#if WORKER_NODE
#endif

// ==========================================
// Module declarations

#if PARTITIONER
module PartitionerService =
#endif

#if SOLVER_RUNNER
module SolverRunner =
#endif

#if WORKER_NODE
module WorkerNodeService =
#endif

// ==========================================
// Code

#if PARTITIONER || SOLVER_RUNNER || WORKER_NODE
#endif

#if PARTITIONER

    let connectionStringKey = ConfigKey "PartitionerService"


    [<Literal>]
    let private DbName = "prt" + VersionNumberNumericalValue

#endif

#if SOLVER_RUNNER
#endif

#if SOLVER_RUNNER || WORKER_NODE

    /// Both WorkerNodeService and SolverRunner use the same database.
    let private connectionStringKey = ConfigKey "WorkerNodeService"


    [<Literal>]
    let private DbName = "wns" + VersionNumberNumericalValue

#endif

#if PARTITIONER || SOLVER_RUNNER || WORKER_NODE

    [<Literal>]
    let private ConnectionStringValue = "Server=localhost;Database=" + DbName + ";Integrated Security=SSPI"

    let private getConnectionStringImpl() = getConnectionString AppSettingsFile connectionStringKey ConnectionStringValue
    let private connectionString = Lazy<ConnectionString>(getConnectionStringImpl)
    let private getConnectionString() = connectionString.Value


    type private Db = SqlDataProvider<
                    Common.DatabaseProviderTypes.MSSQLSERVER,
                    ConnectionString = ConnectionStringValue,
                    UseOptionTypes = Common.NullableColumnType.OPTION>

    type private DbContext = Db.dataContext
    let private getDbContext (c : unit -> ConnectionString) = c().value |> Db.GetDataContext


    //type private RunQueueEntity = DbContext.``dbo.RunQueueEntity``
    //type private MessageEntity = DbContext.``dbo.MessageEntity``
#endif

#if SOLVER_RUNNER

    /// The data is stored as serialized ModelBinaryData.
    /// Here we need to load it as ModelBinaryData, and then elevate to ModelData<'D>.
    let tryLoadRunQueue<'D> (i : RunQueueId) =
        let elevate e = e |> TryLoadRunQueueErr
        let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryLoadRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for q in ctx.Dbo.RunQueue do
                    where (q.RunQueueId = i.value)
                    select (Some (q.ModelData, q.RunQueueStatusId))
                    exactlyOneOrDefault
                }

            match x with
            | Some (v, s) ->
                let w() =
                    try
                        match RunQueueStatus.tryCreate s with
                        | Some st ->
                            match v |> tryDeserialize<ModelBinaryData> serializationFormat with
                            | Ok b ->
                                match ModelData<'D>.tryFromModelBinaryData b with
                                | Ok d -> Ok (d, st)
                                | Error e -> toError (SerializationErr e)
                            | Error e -> toError (SerializationErr e)
                        | None -> toError (InvalidRunQueueStatus (i, s))
                    with
                    | e -> toError (ExnWhenTryLoadRunQueue (i, e))

                //tryRopFun (fun e -> e |> DbExn |> DbErr) w
                tryDbFun fromDbError w
            | None -> toError (UnableToFindRunQueue i)

        tryDbFun fromDbError g


    /// Can transition to InProgress from NotStarted or InProgress (to restart).
    /// If run queue is in RunQueueStatus_CancelRequested state then we don't allow restarting it automatically.
    /// This could happen when cancel requested has propagated to the database but the system then crashed and
    /// did not actually cancel the solver.
    let tryStartRunQueue (q : RunQueueId) (ProcessId pid) =
        let elevate e = e |> TryStartRunQueueErr
        //let toError e = e |> elevate |> Error
        let x e = CannotStartRunQueue e |> elevate
        let fromDbError e = e |> TryStartRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.TryStartRunQueue.Invoke(``@runQueueId`` = q.value, ``@processId`` = pid)
            r.ResultSet |> bindIntScalar x q

        tryDbFun fromDbError g


    let tryCheckCancellation (q : RunQueueId) =
        let elevate e = e |> TryCheckCancellationErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryCheckCancellationDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for x in ctx.Dbo.RunQueue do
                    where (x.RunQueueId = q.value && x.RunQueueStatusId = RunQueueStatus.CancelRequestedRunQueue.value)
                    select (Some x.NotificationTypeId)
                    exactlyOneOrDefault
                }

            match x with
            | Some v ->
                match v with
                | 0 -> AbortCalculation None
                | _ -> CancelWithResults None
                |> Some
            | None -> None
            |> Ok

        tryDbFun fromDbError g


    /// Check for notification only when InProgress
    let tryCheckNotification (q : RunQueueId) =
        let elevate e = e |> TryCheckNotificationErr
        //let toError e = e |> elevate |> Error
        let y e = CannotCheckNotification e |> elevate
        let fromDbError e = e |> TryCheckNotificationDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for x in ctx.Dbo.RunQueue do
                    where (x.RunQueueId = q.value && x.RunQueueStatusId = RunQueueStatus.InProgressRunQueue.value)
                    select (Some x.NotificationTypeId)
                    exactlyOneOrDefault
                }

            match x with
            | Some v ->
                match v with
                | 0 -> None
                | 1 -> Some RegularChartGeneration
                | 2 -> Some ForceChartGeneration
                | _ -> None
                |> Ok
            | None -> y q |> Error

        tryDbFun fromDbError g


    /// Clear notification only when InProgress
    let tryClearNotification (q : RunQueueId) =
        let elevate e = e |> TryClearNotificationErr
        //let toError e = e |> elevate |> Error
        let x e = CannotClearNotification e |> elevate
        let fromDbError e = e |> TryClearNotificationDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.TryClearNotificationRunQueue.Invoke(``@runQueueId`` = q.value)
            r.ResultSet |> bindIntScalar x q

        tryDbFun fromDbError g


    let deleteRunQueue (q : RunQueueId) =
        let elevate e = e |> DeleteRunQueueErr
        //let toError e = e |> elevate |> Error
        let x e = DeleteRunQueueEntryErr e |> elevate
        let fromDbError e = e |> DeleteRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.DeleteRunQueue.Invoke(``@runQueueId`` = q.value)
            r.ResultSet |> bindIntScalar x q

        tryDbFun fromDbError g


    /// Can modify progress related information when state is InProgress or CancelRequested.
    let tryUpdateProgress<'P> (q : RunQueueId) (pd : ProgressData<'P>) =
        let elevate e = e |> TryUpdateProgressErr
        //let toError e = e |> elevate |> Error
        let x e = CannotUpdateProgress e |> elevate
        let fromDbError e = e |> TryUpdateProgressDbErr |> elevate

        let g() =
            printfn $"tryUpdateProgress: RunQueueId: {q}, progress data: %A{pd}."
            let ctx = getDbContext getConnectionString
            let p = pd.toProgressData()

            let progressData =
                match p.progressDetailed with
                | Some d -> serializeProgress d
                | None -> null

            let r = ctx.Procedures.TryUpdateProgressRunQueue.Invoke(
                                        ``@runQueueId`` = q.value,
                                        ``@progress`` = p.progressInfo.progress,
                                        ``@progressData`` = progressData,
                                        ``@callCount`` = p.progressInfo.callCount,
                                        ``@relativeInvariant`` = p.progressInfo.relativeInvariant.value)

            r.ResultSet |> bindIntScalar x q

        tryDbFun fromDbError g

#endif

#if WORKER_NODE

    let tryLoadSolverRunners () =
        let elevate e = e |> TryLoadSolverRunnersErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryLoadSolverRunnersDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for q in ctx.Dbo.RunQueue do
                    where (q.RunQueueStatusId = RunQueueStatus.InProgressRunQueue.value || q.RunQueueStatusId = RunQueueStatus.CancelRequestedRunQueue.value)
                    select (q.RunQueueId, q.ProcessId)
                }

            x
            |> Seq.toList
            |> List.map (fun (r, p) -> { runQueueId = RunQueueId r; processId = p |> Option.bind (fun e -> e |> ProcessId |> Some) })
            |> Ok

        tryDbFun fromDbError g


    let tryGetRunningSolversCount () =
        let elevate e = e |> TryGetRunningSolversCountErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryGetRunningSolversCountDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for q in ctx.Dbo.RunQueue do
                    where (q.RunQueueStatusId = RunQueueStatus.InProgressRunQueue.value || q.RunQueueStatusId = RunQueueStatus.CancelRequestedRunQueue.value)
                    select q
                    count
                }

            Ok x

        tryDbFun fromDbError g


    let tryPickRunQueue () =
        let elevate e = e |> TryPickRunQueueErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryPickRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for q in ctx.Dbo.RunQueue do
                    where (q.RunQueueStatusId = RunQueueStatus.NotStartedRunQueue.value)
                    sortBy q.RunQueueOrder
                    select (Some q.RunQueueId)
                    exactlyOneOrDefault
                }

            x |> Option.map RunQueueId |> Ok

        tryDbFun fromDbError g


    let loadAllActiveRunQueueId () =
        let elevate e = e |> LoadAllActiveRunQueueIdErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> LoadAllActiveRunQueueIdDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for q in ctx.Dbo.RunQueue do
                    where (q.RunQueueStatusId =
                        RunQueueStatus.NotStartedRunQueue.value
                        || q.RunQueueStatusId = RunQueueStatus.InProgressRunQueue.value
                        || q.RunQueueStatusId = RunQueueStatus.CancelRequestedRunQueue.value)
                    select q.RunQueueId
                }

            x
            |> Seq.toList
            |> List.map RunQueueId
            |> Ok

        tryDbFun fromDbError g


    let private addRunQueueRow<'D> (ctx : DbContext) (r : RunQueueId) (w : ModelBinaryData) =
        let row = ctx.Dbo.RunQueue.Create(
                            RunQueueId = r.value,
                            ModelData = (w |> serialize serializationFormat),
                            RunQueueStatusId = RunQueueStatus.NotStartedRunQueue.value,
                            CreatedOn = DateTime.Now,
                            ModifiedOn = DateTime.Now)

        row


    /// Saves intocoming model data into a database fur further processing.
    let saveModelData<'D> (r : RunQueueId) (w : ModelBinaryData) =
        let elevate e = e |> SaveRunQueueErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> SaveRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let row = addRunQueueRow ctx r w
            ctx.SubmitUpdates()

            Ok()

        tryDbFun fromDbError g


    /// Can transition to Completed from InProgress or CancelRequested.
    let tryCompleteRunQueue (q : RunQueueId) =
        let elevate e = e |> TryCompleteRunQueueErr
        //let toError e = e |> elevate |> Error
        let x e = CannotCompleteRunQueue e |> elevate
        let fromDbError e = e |> TryCompleteRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.TryCompleteRunQueue.Invoke(``@runQueueId`` = q.value)
            r.ResultSet |> bindIntScalar x q

        tryDbFun fromDbError g


    /// Can transition to Cancelled from NotStarted, InProgress, or CancelRequested.
    let tryCancelRunQueue (q : RunQueueId) (errMsg : string) =
        let elevate e = e |> TryCancelRunQueueErr
        //let toError e = e |> elevate |> Error
        let x e = TryCancelRunQueueError.CannotCancelRunQueue e |> elevate
        let fromDbError e = e |> TryCancelRunQueueError.TryCancelRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.TryCancelRunQueue.Invoke(``@runQueueId`` = q.value, ``@errorMessage`` = errMsg)
            r.ResultSet |> bindIntScalar x q

        tryDbFun fromDbError g


    /// Can transition to Failed from InProgress or CancelRequested.
    let tryFailRunQueue (q : RunQueueId) (errMsg : string) =
        let elevate e = e |> TryFailRunQueueErr
        //let toError e = e |> elevate |> Error
        let x e = CannotFailRunQueue e |> elevate
        let fromDbError e = e |> TryFailRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.TryFailRunQueue.Invoke(``@runQueueId`` = q.value, ``@errorMessage`` = errMsg)
            r.ResultSet |> bindIntScalar x q

        tryDbFun fromDbError g


    /// Can transition to CancelRequested from InProgress.
    let tryRequestCancelRunQueue (q : RunQueueId) (r : CancellationType) =
        let elevate e = e |> TryRequestCancelRunQueueErr
        //let toError e = e |> elevate |> Error
        let x e = CannotRequestCancelRunQueue e |> elevate
        let fromDbError e = e |> TryRequestCancelRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.TryRequestCancelRunQueue.Invoke(``@runQueueId`` = q.value, ``@notificationTypeId`` = r.value)
            r.ResultSet |> bindIntScalar x q

        tryDbFun fromDbError g


    /// Can request notification of results when state is InProgress or CancelRequested.
    let tryNotifyRunQueue (q : RunQueueId) (r : ChartNotificationType option) =
        let elevate e = e |> TryNotifyRunQueueErr
        //let toError e = e |> elevate |> Error
        let x e = CannotNotifyRunQueue e |> elevate
        let fromDbError e = e |> TryNotifyRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let v = r |> Option.bind (fun e -> Some e.value) |> Option.defaultValue 0
            let r = ctx.Procedures.TryNotifyRunQueue.Invoke(``@runQueueId`` = q.value, ``@notificationTypeId`` = v)
            r.ResultSet |> bindIntScalar x q

        tryDbFun fromDbError g

#endif
