#nowarn "1104"
namespace Softellect.DistributedProcessing.DataAccess

open System
open System.Threading
open System.IO

open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Rop
open Softellect.Sys.TimerEvents
open Softellect.Sys.Errors
open Softellect.Sys.Logging
open Softellect.Sys.Crypto

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
open Softellect.Sys.VersionInfo
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.Primitives.Common

// ==========================================
// Blank #if template blocks

#if PARTITIONER
#endif

#if MODEL_GENERATOR
#endif

#if PARTITIONER || MODEL_GENERATOR
#endif

#if SOLVER_RUNNER || WORKER_NODE
#endif

#if SOLVER_RUNNER
#endif

#if WORKERNODE_ADM
#endif

#if WORKER_NODE
#endif

#if PARTITIONER || PARTITIONER_ADM || MODEL_GENERATOR || SOLVER_RUNNER || WORKERNODE_ADM || WORKER_NODE
#endif

// ==========================================
// Open declarations

#if PARTITIONER
open Softellect.DistributedProcessing.Primitives.PartitionerService
open Softellect.Sys
open System.IO
#endif

#if PARTITIONER_ADM
open Softellect.DistributedProcessing.Primitives.PartitionerAdm
#endif

#if SOLVER_RUNNER
open Softellect.DistributedProcessing.SolverRunner.Primitives
#endif

#if WORKER_NODE
open Softellect.DistributedProcessing.Primitives.WorkerNodeService
#endif

// ==========================================
// Module declarations

#if PARTITIONER
module PartitionerService =
#endif

#if PARTITIONER_ADM
module PartitionerAdm =
#endif

#if MODEL_GENERATOR
module ModelGenerator =
#endif

#if SOLVER_RUNNER
module SolverRunner =
#endif

#if WORKERNODE_ADM
module WorkerNodeAdm =
#endif

#if WORKER_NODE
module WorkerNodeService =
#endif

// ==========================================
// Common for all.

#if PARTITIONER || PARTITIONER_ADM || MODEL_GENERATOR || SOLVER_RUNNER || WORKERNODE_ADM || WORKER_NODE

    let private partitionerPublicKeySettingName = SettingName "395A5869D3104C2D9FD87421B501D622"
    let private partitionerPrivateKeySettingName = SettingName "EC25A0A61B4749938BC80B833BBA351D"

    let private workerNodePublicKeySettingName = SettingName "BD95B603BBFB4C5BB8C74A4C36783EB3"
    let private workerNodePrivateKeySettingName = SettingName "A895B4BDC0354D6EB776D26DD30BA943"

    let private workerNodeReinstallationSettingName = SettingName "54A1D285-7F93-4242-9E07-E99F90CBF898"

#endif

// ==========================================
// Database access

#if PARTITIONER || PARTITIONER_ADM || MODEL_GENERATOR

    /// Both Partitioner, PartitionerAdm, and ModelGenerator use the same database.
    let connectionStringKey = ConfigKey "PartitionerService"


    [<Literal>]
    let private DbName = "prt" + VersionNumberNumericalValue

#endif

#if SOLVER_RUNNER || WORKERNODE_ADM || WORKER_NODE

    /// Both WorkerNodeService and SolverRunner use the same database.
    let private connectionStringKey = ConfigKey "WorkerNodeService"


    [<Literal>]
    let private DbName = "wns" + VersionNumberNumericalValue

#endif

#if PARTITIONER || PARTITIONER_ADM || MODEL_GENERATOR || SOLVER_RUNNER || WORKERNODE_ADM || WORKER_NODE

    [<Literal>]
    let private ConnectionStringValue = "Server=localhost;Database=" + DbName + ";Integrated Security=SSPI;TrustServerCertificate=yes;"

    let private getConnectionStringImpl() = getConnectionString connectionStringKey ConnectionStringValue
    let private connectionString = Lazy<ConnectionString>(getConnectionStringImpl)
    let private getConnectionString() = connectionString.Value


    type private Db = SqlDataProvider<
                    Common.DatabaseProviderTypes.MSSQLSERVER,
                    ConnectionString = ConnectionStringValue,
                    UseOptionTypes = Common.NullableColumnType.OPTION>

    type private DbContext = Db.dataContext
    let private getDbContext (c : unit -> ConnectionString) = c().value |> Db.GetDataContext


    type private RunQueueEntity = DbContext.``dbo.RunQueueEntity``
    type private MessageEntity = DbContext.``dbo.MessageEntity``
    type private ModelDataEntity = DbContext.``dbo.ModelDataEntity``
    type private SettingEntity = DbContext.``dbo.SettingEntity``

#endif

// ==========================================
// Specific Types

#if PARTITIONER || PARTITIONER_ADM || WORKERNODE_ADM || WORKER_NODE

    type private SolverEntity = DbContext.``dbo.SolverEntity``
    type private SettingDataEntity = DbContext.``dbo.SettingEntity``

#endif

#if PARTITIONER || PARTITIONER_ADM

    type private WorkerNodeEntity = DbContext.``dbo.WorkerNodeEntity``

#endif

// ==========================================
// Code

#if PARTITIONER || PARTITIONER_ADM || MODEL_GENERATOR || SOLVER_RUNNER || WORKERNODE_ADM || WORKER_NODE

    /// Combines pieces of LocationDetailedInfo into a full path.
    let getFileLocation (location : LocationDetailedInfo) (fileName : FileName) =
        try
            let i = location.locationInfo
            match location.optionalFolder with
            | Some (FolderName f) -> Path.Combine(i.location.value, i.solverName.value, f, fileName.value)
            | None -> Path.Combine(i.location.value, i.solverName.value, fileName.value)
            |> FileName
            |> Ok
        with
        | e ->
            Logger.logError $"location: %A{location}, exception: %A{e}."
            e |> GetLocationExn |> GetLocationErr |> Error


    let getFolderLocation (location : LocationDetailedInfo)=
        try
            let i = location.locationInfo
            match location.optionalFolder with
            | Some (FolderName f) -> Path.Combine(i.location.value, i.solverName.value, f)
            | None -> Path.Combine(i.location.value, i.solverName.value)
            |> FolderName
            |> Ok
        with
        | e ->
            Logger.logError $"location: %A{location}, exception: %A{e}."
            e |> GetLocationExn |> GetLocationErr |> Error

#endif

#if PARTITIONER || PARTITIONER_ADM || WORKERNODE_ADM || WORKER_NODE

    let tryLoadSetting (SettingName keyName) =
        let elevate e = e |> TryLoadSettingErr
        let fromDbError e = e |> TryLoadSettingDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for s in ctx.Dbo.Setting do
                    where (s.SettingName = keyName)
                    select (Some s)
                    exactlyOneOrDefault
                }

            match x with
            | Some v ->
                {
                    settingName = v.SettingName |> SettingName
                    settingBool = v.SettingBool
                    settingGuid = v.SettingGuid
                    settingLong = v.SettingLong
                    settingText = v.SettingText
                    settingBinary = v.SettingBinary
                    createdOn = v.CreatedOn
                }
                |> Some
                |> Ok
            | None -> Ok None

        tryDbFun fromDbError g


    /// TODO kk:20250422 - Mysterious error when trying to save an setting.
    ///     Implicit conversion from data type nvarchar to varbinary(max) is not allowed. Use the CONVERT function to run this query.
    let trySaveSetting (setting : Setting) =
        let elevate e = e |> TrySaveSettingErr
        let fromDbError e = e |> TrySaveSettingDbErr |> elevate

        let update (existing : bool) (s : SettingEntity) =
            if not existing then s.SettingName <- setting.settingName.value
            s.SettingBool <- setting.settingBool
            s.SettingGuid <- setting.settingGuid
            s.SettingLong <- setting.settingLong
            s.SettingText <- setting.settingText
            s.SettingBinary <- setting.settingBinary
            s.CreatedOn <- DateTime.Now

        let g() =
            let ctx = getDbContext getConnectionString

            let eso =
                query {
                    for s in ctx.Dbo.Setting do
                    where (s.SettingName = setting.settingName.value)
                    select (Some s)
                    exactlyOneOrDefault
                }

            match eso with
            | Some existingSetting -> update true existingSetting
            | None -> ctx.Dbo.Setting.Create() |> update false

            ctx.SubmitUpdates()
            Ok ()

        tryDbFun fromDbError g


    let tryDeleteSetting (SettingName keyName) =
        let elevate e = e |> TryDeleteSettingErr
        let fromDbError e = e |> TryDeleteSettingDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let settingToDelete =
                query {
                    for s in ctx.Dbo.Setting do
                    where (s.SettingName = keyName)
                    select s
                    exactlyOneOrDefault
                }

            match settingToDelete with
            | null -> Ok false  // Setting not found, nothing to delete
            | setting ->
                setting.Delete()
                ctx.SubmitUpdates()
                Ok true  // Successfully deleted

        tryDbFun fromDbError g


    /// Tries loading a public or private key out of setting using a given key name.
    let private tryLoadEncryptionKey toKey (SettingName keyName) =
        let elevate e = e |> TryLoadEncryptionKeyErr
        let fromDbError e = e |> TryLoadEncryptionKeyDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for s in ctx.Dbo.Setting do
                    where (s.SettingName = keyName)
                    select s.SettingBinary
                    exactlyOneOrDefault
                }

            match x with
            | Some v -> v |> unZip |> toKey |> Some |> Ok
            | None -> Ok None

        tryDbFun fromDbError g


    let private trySaveEncryptionKey fromKey (SettingName keyName) encryptionKey =
        let elevate e = e |> TrySaveEncryptionKeyErr
        let fromDbError e = e |> TrySaveEncryptionKeyDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            // Retrieve the current key if it exists
            let existingKey =
                query {
                    for s in ctx.Dbo.Setting do
                    where (s.SettingName = keyName)
                    select (Some s)
                    exactlyOneOrDefault
                }

            let settingBinary = encryptionKey |> fromKey |> zip |> Some

            match existingKey with
            | Some setting ->
                // Update the existing key
                setting.SettingBinary <- settingBinary
                setting.CreatedOn <- DateTime.Now
            | None ->
                // Insert a new key
                let newSetting = ctx.Dbo.Setting.Create()
                newSetting.SettingName <- keyName
                newSetting.SettingBinary <- settingBinary
                newSetting.CreatedOn <- DateTime.Now

            // Save changes to the database
            ctx.SubmitUpdates()
            Ok ()

        tryDbFun fromDbError g

#endif

#if PARTITIONER || PARTITIONER_ADM || WORKER_NODE

    let tryGetSolverName (r : RunQueueId) =
        let elevate e = e |> TryGetSolverNameErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryGetSolverNameDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for q in ctx.Dbo.RunQueue do
                    join s in ctx.Dbo.Solver on (q.SolverId = s.SolverId)
                    where (q.RunQueueId = r.value)
                    select (Some s.SolverName)
                    exactlyOneOrDefault
                }

            x |> Option.map SolverName |> Ok

        tryDbFun fromDbError g


    let private processRunQueue (runQueueId : RunQueueId) m f =
        let ctx = getDbContext getConnectionString

        let x =
            query {
                for s in ctx.Dbo.RunQueue do
                where (s.RunQueueId = runQueueId.value)
                select (Some s)
                exactlyOneOrDefault
            }

        match x with
        | Some s -> m ctx s
        | None -> f ctx


    let private mapSolver (s : SolverEntity) =
        {
            solverId = SolverId s.SolverId
            solverName = SolverName s.SolverName
            description = s.Description
            solverData = s.SolverData |> Option.map (fun e -> SolverData e)

#if PARTITIONER || PARTITIONER_ADM
            solverHash = s.SolverHash |> Sha256Hash
#endif

#if WORKER_NODE
            // We don't really care about solver hash for Worker Node, but we need to have it for consistency.
            solverHash = s.SolverData |> Option.defaultValue [||] |> calculateSha256Hash
#endif
        }


    let private processSolver (solverId : SolverId) m f =
        let ctx = getDbContext getConnectionString

        let x =
            query {
                for s in ctx.Dbo.Solver do
                where (s.SolverId = solverId.value)
                select (Some s)
                exactlyOneOrDefault
            }

        match x with
        | Some s -> m ctx s
        | None -> f ctx


    let tryLoadSolver (solverId : SolverId) =
        let elevate e = e |> MapSolverErr
        let fromDbError e = e |> MapSolverDbErr |> elevate
        let g() = processSolver solverId (fun _ s -> mapSolver s |> Ok) (fun _ -> Error (SolverNotFound solverId))
        tryDbFun fromDbError g


    let private addSolverRow (ctx : DbContext) (s : Solver) =
        let row = ctx.Dbo.Solver.Create(
                            SolverId = s.solverId.value,
                            SolverName = s.solverName.value,
                            Description = s.description,
#if PARTITIONER || PARTITIONER_ADM
                            // Partitioner has separate solverHash and isInactive columns.
                            SolverHash = s.solverHash.value,
                            IsInactive = false,
#endif

#if WORKER_NODE
                            // Worker Node has a separate isDeployed flag.
                            IsDeployed = false,
#endif
                            CreatedOn = DateTime.Now,
                            SolverData = (s.solverData |> Option.map (fun e -> e.value)))

        row


    /// Saves incoming solver into a database for further processing.
    let saveSolver (solver : Solver) =
        let elevate e = e |> SaveSolverErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> SaveSolverDbErr |> elevate

        let g() =
            processSolver
                solver.solverId
                (fun ctx s ->
                    s.SolverName <- solver.solverName.value
                    s.Description <- solver.description
                    s.SolverData <- (solver.solverData |> Option.map (fun e -> e.value))
                    s.CreatedOn <- DateTime.Now

#if PARTITIONER || PARTITIONER_ADM
                    // Partitioner has separate solverHash and isInactive columns.
                    s.SolverHash <- solver.solverHash.value
                    s.IsInactive <- false
#endif

#if WORKER_NODE
                    // Worker Node has a separate isDeployed flag.
                    s.IsDeployed <- false
#endif

                    ctx.SubmitUpdates()
                    Ok())
                (fun ctx ->
                    let _ = addSolverRow ctx solver

                    ctx.SubmitUpdates()
                    Ok ())

        tryDbFun fromDbError g


    let unpackSolver folderName (solver : Solver) =
        match solver.solverData with
        | Some data ->
            match unzipToFolder data.value folderName true with
            | Ok _ -> Ok ()
            | Error e -> UnableToDeploySolverErr (solver.solverId, folderName, e) |> SaveSolverErr |> Error
        | None -> Ok()

#endif

#if PARTITIONER || PARTITIONER_ADM

    let checkIfSolverDeployed (solverId : SolverId) (w : WorkerNodeId) =
        let elevate e = e |> CheckIfSolverDeployedErr
        let fromDbError e = e |> CheckIfSolverDeployedDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for s in ctx.Dbo.WorkerNodeSolver do
                    where (s.SolverId = solverId.value && s.WorkerNodeId = w.value.value && s.IsDeployed)
                    select (Some s.WorkerNodeId)
                    exactlyOneOrDefault
                }

            match x with
            | Some _ -> Ok true
            | None -> Ok false

        tryDbFun fromDbError g


    let getSolverHash (solverId : SolverId) =
        let elevate e = e |> GetSolverHashErr
        let fromDbError e = e |> GetSolverHashDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for s in ctx.Dbo.Solver do
                    where (s.SolverId = solverId.value)
                    select (Some s.SolverHash)
                    exactlyOneOrDefault
                }

            match x with
            | Some v -> v |> Sha256Hash |> Some |> Ok
            | None -> Ok None

        tryDbFun fromDbError g


    let private mapRunQueue (r: RunQueueEntity) =
        let elevate e = e |> MapRunQueueErr
        let toError e = e |> elevate |> Error
        //let fromDbError e = e |> MapRunQueueDbErr |> elevate

        let runQueueId = RunQueueId r.RunQueueId

        match RunQueueStatus.tryCreate r.RunQueueStatusId with
        | Some s ->
            {
                runQueueId = runQueueId
                runQueueStatus = s
                solverId = SolverId r.SolverId
                workerNodeIdOpt = r.WorkerNodeId |> Option.bind (fun e -> e |> MessagingClientId |> WorkerNodeId |> Some)

                progressData =
                    {
                        progressInfo =
                            {
                                progress = r.Progress
                                callCount = r.CallCount
                                processId = r.ProcessId |> Option.bind (fun e -> e |> ProcessId |> Some)
                                evolutionTime = EvolutionTime r.EvolutionTime
                                relativeInvariant = RelativeInvariant r.RelativeInvariant
                                errorMessageOpt = r.ErrorMessage |> Option.map ErrorMessage
                            }

                        progressDetailed = None
                    }

                createdOn = r.CreatedOn
                lastErrorOn = r.LastErrorOn
                retryCount = r.RetryCount
                maxRetries = r.MaxRetries
            }
            |> Ok
        | None -> toError (CannotMapRunQueue runQueueId)


    /// Loads RunQueue by runQueueId.
    let tryLoadRunQueue (q : RunQueueId) =
        let elevate e = e |> TryLoadRunQueueErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryLoadRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for r in ctx.Dbo.RunQueue do
                    where (r.RunQueueId = q.value)
                    select (Some r)
                    exactlyOneOrDefault
                }

            match x with
            | Some v ->
                match mapRunQueue v with
                | Ok r -> Ok (Some r)
                | Error e -> Error e
            | None -> Ok None

        tryDbFun fromDbError g


    let private addRunQueueRow (ctx : DbContext) (r : RunQueue) =
        let row = ctx.Dbo.RunQueue.Create(
                            RunQueueId = r.runQueueId.value,
                            SolverId = r.solverId.value,
                            WorkerNodeId = (r.workerNodeIdOpt |> Option.bind (fun e -> Some e.value.value)),
                            RunQueueStatusId = r.runQueueStatus.value,
                            ProcessId = (r.progressData.progressInfo.processId |> Option.bind (fun e -> Some e.value)),
                            ErrorMessage = (r.progressData.progressInfo.errorMessageOpt |> Option.bind (fun e -> Some e.value)),
                            RetryCount = r.retryCount,
                            MaxRetries = r.maxRetries,
                            Progress = r.progressData.progressInfo.progress,
                            CallCount = r.progressData.progressInfo.callCount,
                            EvolutionTime = r.progressData.progressInfo.evolutionTime.value,
                            RelativeInvariant = r.progressData.progressInfo.relativeInvariant.value,
                            CreatedOn = DateTime.Now,
                            ModifiedOn = DateTime.Now)

        row


    /// The following transitions are allowed here:
    ///
    ///     NotStartedRunQueue + None (workerNodeId) -> RunRequestedRunQueue + Some workerNodeId - scheduled (but not yet confirmed) new work.
    ///     NotStartedRunQueue + None (workerNodeId) -> CancelledRunQueue + None (workerNodeId) - cancelled work that has not been scheduled yet.
    ///
    ///     RunRequestedRunQueue + Some workerNodeId -> NotStartedRunQueue + None (workerNodeId) - the node rejected work.
    ///     RunRequestedRunQueue + Some workerNodeId -> InProgressRunQueue + the same Some workerNodeId - the node accepted work.
    ///     RunRequestedRunQueue + Some workerNodeId -> CancelRequestedRunQueue + the same Some workerNodeId -
    ///          scheduled (but not yet confirmed) new work, which then was requested to be cancelled before the node replied.
    ///     + -> completed / failed
    ///
    ///     InProgressRunQueue -> InProgressRunQueue + the same Some workerNodeId - normal work progress.
    ///     InProgressRunQueue -> CompletedRunQueue + the same Some workerNodeId (+ the progress will be updated to Completed _) - completed work.
    ///     InProgressRunQueue -> FailedRunQueue + the same Some workerNodeId - failed work.
    ///     InProgressRunQueue -> CancelRequestedRunQueue + the same Some workerNodeId - request for cancellation of actively running work.
    ///
    ///     CancelRequestedRunQueue -> CancelRequestedRunQueue + the same Some workerNodeId - repeated cancel request.
    ///     CancelRequestedRunQueue -> InProgressRunQueue + the same Some workerNodeId -
    ///         roll back to cancel requested - in progress message came while our cancel request propagates through the system.
    ///     CancelRequestedRunQueue -> CancelledRunQueue + the same Some workerNodeId - the work has been successfully cancelled.
    ///     CancelRequestedRunQueue -> CompletedRunQueue + the same Some workerNodeId - the node completed work before cancel request propagated through the system.
    ///     CancelRequestedRunQueue -> FailedRunQueue + the same Some workerNodeId - the node failed before cancel request propagated through the system.
    ///
    /// All others are not allowed and / or out of scope of this function.
    let private tryUpdateRunQueueRow (r : RunQueueEntity) (q : RunQueue) =
        let elevate e = e |> TryUpdateRunQueueRowErr
        let toError e = e |> elevate |> Error
        //let fromDbError e = e |> TryUpdateRunQueueRowDbErr |> elevate
        //let toError e = e |> RunQueueTryUpdateRowErr |> DbErr |> Error

        let g s u =
            r.WorkerNodeId <- (q.workerNodeIdOpt |> Option.bind (fun e -> Some e.value.value))
            r.Progress <- q.progressData.progressInfo.progress
            r.ProcessId <- q.progressData.progressInfo.processId |> Option.bind (fun e -> Some e.value)
            r.EvolutionTime <- q.progressData.progressInfo.evolutionTime.value
            r.CallCount <- q.progressData.progressInfo.callCount
            r.RelativeInvariant <- q.progressData.progressInfo.relativeInvariant.value
            r.ErrorMessage <- q.progressData.progressInfo.errorMessageOpt |> Option.bind (fun e -> Some e.value)

            match s with
            | Some (Some v) -> r.StartedOn <- Some v
            | Some None-> r.StartedOn <- None
            | None -> ()

            r.ModifiedOn <- DateTime.Now

            match u with
            | true -> r.RunQueueStatusId <- q.runQueueStatus.value
            | false -> ()

            Ok()

        let f s =
            {
                runQueueId = q.runQueueId
                runQueueStatusFrom = s
                runQueueStatusTo = q.runQueueStatus
                workerNodeIdOptFrom = r.WorkerNodeId |> Option.bind (fun e -> e |> MessagingClientId |> WorkerNodeId |> Some)
                workerNodeIdOptTo = q.workerNodeIdOpt
            }

        let f1 e = e |> InvalidStatusTransitionErr |> toError
        let f2 e = e |> InvalidDataErr |> toError

        match RunQueueStatus.tryCreate r.RunQueueStatusId with
        | Some s ->
            match s, r.WorkerNodeId, q.runQueueStatus, q.workerNodeIdOpt with
            | NotStartedRunQueue,       None,    RunRequestedRunQueue,   Some _ -> g (Some (Some DateTime.Now)) true
            | NotStartedRunQueue,       None,    CancelledRunQueue,      None -> g None true

            | RunRequestedRunQueue,   Some _, NotStartedRunQueue,       None -> g (Some None) true
            | RunRequestedRunQueue,   Some w1, InProgressRunQueue,       Some w2 when w1 = w2.value.value -> g None true
            | RunRequestedRunQueue,   Some w1, CancelRequestedRunQueue,  Some w2 when w1 = w2.value.value -> g None true
            | RunRequestedRunQueue,   Some w1, CompletedRunQueue,        Some w2 when w1 = w2.value.value -> g None true
            | RunRequestedRunQueue,   Some w1, FailedRunQueue,           Some w2 when w1 = w2.value.value -> g None true

            | InProgressRunQueue,      Some w1, InProgressRunQueue,      Some w2 when w1 = w2.value.value -> g None true
            | InProgressRunQueue,      Some w1, CompletedRunQueue,       Some w2 when w1 = w2.value.value -> g None true
            | InProgressRunQueue,      Some w1, FailedRunQueue,          Some w2 when w1 = w2.value.value -> g None true
            | InProgressRunQueue,      Some w1, CancelRequestedRunQueue, Some w2 when w1 = w2.value.value -> g None true

            | CancelRequestedRunQueue, Some w1, CancelRequestedRunQueue, Some w2 when w1 = w2.value.value -> g None true
            | CancelRequestedRunQueue, Some w1, InProgressRunQueue,      Some w2 when w1 = w2.value.value -> g None false // !!! Roll back the status change !!!
            | CancelRequestedRunQueue, Some w1, CancelledRunQueue,       Some w2 when w1 = w2.value.value -> g None true
            | CancelRequestedRunQueue, Some w1, CompletedRunQueue,       Some w2 when w1 = w2.value.value -> g None true
            | CancelRequestedRunQueue, Some w1, FailedRunQueue,          Some w2 when w1 = w2.value.value -> g None true
            | _ -> s |> Some |> f |> f1
        | None -> None |> f |> f2


    let upsertRunQueue (w : RunQueue) =
        let elevate e = e |> UpsertRunQueueErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> UpsertRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for r in ctx.Dbo.RunQueue do
                    where (r.RunQueueId = w.runQueueId.value)
                    select (Some r)
                    exactlyOneOrDefault
                }

            let result =
                match x with
                | Some v -> tryUpdateRunQueueRow v w
                | None -> addRunQueueRow ctx w |> ignore; Ok()

            ctx.SubmitUpdates()
            result

        tryDbFun fromDbError g


    let tryLoadWorkerNodePublicKey (w : WorkerNodeId) : DistributedProcessingResult<PublicKey option> =
        let elevate e = e |> TryLoadWorkerNodePublicKeyErr
        let fromDbError e = e |> TryLoadWorkerNodePublicKeyDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for s in ctx.Dbo.WorkerNode do
                    where (s.WorkerNodeId = w.value.value)
                    select s.WorkerNodePublicKey
                    exactlyOneOrDefault
                }

            match x with
            | Some v -> v |> unZip |> PublicKey |> Some |> Ok
            | None -> Ok None

        tryDbFun fromDbError g


    let tryUpdateWorkerNodePublicKey (w: WorkerNodeId) (PublicKey newPublicKey) =
        let elevate e = e |> TryUpdateWorkerNodePublicKeyErr
        let fromDbError e = e |> TryUpdateWorkerNodePublicKeyDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            // Retrieve the row for the specified WorkerNodeId
            let workerNode =
                query {
                    for s in ctx.Dbo.WorkerNode do
                    where (s.WorkerNodeId = w.value.value)
                    select (Some s)
                    exactlyOneOrDefault
                }

            match workerNode with
            | Some row ->
                // Update the public key for the WorkerNode
                row.WorkerNodePublicKey <- newPublicKey |> zip |> Some
                ctx.SubmitUpdates()  // Persist changes to the database
                Ok ()
            | None ->
                // The row must exist; if not, return an error
                w |> UnableToFindWorkerNodeErr |> elevate |> Error

        tryDbFun fromDbError g


    let tryLoadPartitionerPrivateKey () = tryLoadEncryptionKey PrivateKey partitionerPrivateKeySettingName
    let tryLoadPartitionerPublicKey () = tryLoadEncryptionKey PublicKey partitionerPublicKeySettingName

    let trySavePartitionerPrivateKey (PrivateKey privateKey) = trySaveEncryptionKey id partitionerPrivateKeySettingName privateKey
    let trySavePartitionerPublicKey (PublicKey publicKey) = trySaveEncryptionKey id partitionerPublicKeySettingName publicKey

#endif

#if WORKERNODE_ADM || WORKER_NODE

    let tryLoadWorkerNodePublicKey () = tryLoadEncryptionKey PublicKey workerNodePublicKeySettingName
    let tryLoadWorkerNodePrivateKey () = tryLoadEncryptionKey PrivateKey workerNodePrivateKeySettingName

    let trySaveWorkerNodePublicKey (PublicKey publicKey) = trySaveEncryptionKey id workerNodePublicKeySettingName publicKey
    let trySaveWorkerNodePrivateKey (PrivateKey privateKey) = trySaveEncryptionKey id workerNodePrivateKeySettingName privateKey

    let tryLoadPartitionerPublicKey () = tryLoadEncryptionKey PublicKey partitionerPublicKeySettingName
    let trySavePartitionerPublicKey (PublicKey publicKey) = trySaveEncryptionKey id partitionerPublicKeySettingName publicKey

#endif

#if MODEL_GENERATOR || WORKER_NODE

    let private addRunQueueRow (ctx : DbContext) (r : RunQueueId) (s : SolverId) (w : ModelBinaryData) =
        let row = ctx.Dbo.RunQueue.Create(
                            RunQueueId = r.value,
                            SolverId = s.value,
                            RunQueueStatusId = RunQueueStatus.NotStartedRunQueue.value,
                            MaxRetries = defaultMaxRetries,
                            CreatedOn = DateTime.Now,
                            ModifiedOn = DateTime.Now)

        let md = ctx.Dbo.ModelData.Create(
                            RunQueueId = r.value,
                            ModelData = (w |> serializeData))

        (row, md)


    /// Saves new model data into a database fur further processing.
    let saveModelData (r : RunQueueId) (s : SolverId) (w : ModelBinaryData) =
        let elevate e = e |> SaveRunQueueErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> SaveRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            addRunQueueRow ctx r s w |> ignore
            ctx.SubmitUpdates()

            Ok()

        tryDbFun fromDbError g

#endif

#if SOLVER_RUNNER || WORKER_NODE

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

#endif

#if PARTITIONER

    let saveLocalResultInfo (d : LocationInfo) (i : ResultInfo) =
        Logger.logTrace (fun () -> $"saveLocalResultInfo - d: '%A{d}', '%A{i.info}'.")
        let location = { locationInfo = d; optionalFolder = i.optionalFolder }

        try
            let saveResult (c : CalculationResult) =
                let fileName = Path.GetFileName c.fileName.value |> FileName

                match getFileLocation location fileName with
                | Ok fn ->
                    let folder = Path.GetDirectoryName fn.value
                    Directory.CreateDirectory(folder) |> ignore
                    Logger.logTrace (fun () -> $"saveLocalResultInfo - saveResult - fn: '%A{fn}', c: '%A{c.info}'.")

                    match c with
                    | TextResult h -> File.WriteAllText(fn.value, h.textContent)
                    | BinaryResult b -> File.WriteAllBytes(fn.value, b.binaryContent)
                    Ok()
                | Error e ->
                    Logger.logError $"Error saving result, c: '%A{c.info}', exception: '%A{e}'."
                    Error e

            i.results
            |> List.map (fun e -> saveResult e)
            |> ignore
            Ok ()
        with
        | e ->
            Logger.logError $"saveLocalResultInfo - e: '%A{e}'."
            e |> SaveResultsExn |> Error


    /// TODO kk:20241007 - Change errors???
    let loadModelBinaryData (i : RunQueueId) =
        let elevate e = e |> TryLoadRunQueueErr
        let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryLoadRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for q in ctx.Dbo.RunQueue do
                    join m in ctx.Dbo.ModelData on (q.RunQueueId = m.RunQueueId)
                    where (q.RunQueueId = i.value)
                    select (Some (m.ModelData))
                    exactlyOneOrDefault
                }

            match x with
            | Some v ->
                let w() =
                    try
                        match v |> tryDeserializeData<ModelBinaryData> with
                        | Ok b -> Ok b
                        | Error e -> toError (SerializationErr e)
                    with
                    | e -> toError (ExnWhenTryLoadRunQueue (i, e))

                tryDbFun fromDbError w
            | None -> toError (UnableToFindRunQueue i)

        tryDbFun fromDbError g


    /// Loads first not started RunQueue.
    /// We may or may not have a RunQueue to process.
    let tryLoadFirstRunQueue () =
        let elevate e = e |> TryLoadFirstRunQueueErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryLoadFirstRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for q in ctx.Dbo.RunQueue do
                    where (q.RunQueueStatusId = 0 && q.Progress = 0.0m && q.WorkerNodeId = None)
                    sortBy q.RunQueueOrder
                    select (Some q)
                    headOrDefault
                }

            match x with
            | Some v ->
                match mapRunQueue v with
                | Ok r -> Ok (Some r)
                | Error e -> Error e
            | None -> Ok None

        tryDbFun fromDbError g


    /// Gets the first available worker node to schedule work.
    let tryGetAvailableWorkerNode (LastAllowedNodeErr m) (s : SolverId) =
        let elevate e = e |> TryGetAvailableWorkerNodeErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryGetAvailableWorkerNodeDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for r in ctx.Dbo.VwAvailableWorkerNode do
                    where (r.SolverId = s.value && r.WorkLoad < 1.0m && (r.LastErrMinAgo = None || r.LastErrMinAgo.Value < (m / 1<minute>)))
                    sortByDescending r.NodePriority
                    thenBy r.WorkLoad
                    thenBy r.OrderId
                    select (Some r)
                    headOrDefault
                }

            match x with
            | Some r -> r.WorkerNodeId |> MessagingClientId |> WorkerNodeId |> Some |> Ok
            | None -> Ok None

        tryDbFun fromDbError g


    let private createWorkerNodeInfo (p : PartitionerInfo) (r : WorkerNodeEntity) =
        {
            workerNodeId = r.WorkerNodeId |> MessagingClientId |> WorkerNodeId
            workerNodeName = r.WorkerNodeName |> WorkerNodeName
            nodePriority = r.NodePriority |> WorkerNodePriority
            noOfCores = r.NumberOfCores
            partitionerId = p.partitionerId
            isInactive = r.IsInactive
            solverEncryptionType = p.solverEncryptionType
        }


    let loadWorkerNodeInfo (p : PartitionerInfo) (i : WorkerNodeId) =
        let elevate e = e |> LoadWorkerNodeInfoErr
        let toError e = e |> elevate |> Error
        let fromDbError e = e |> LoadWorkerNodeInfoDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for w in ctx.Dbo.WorkerNode do
                    where (w.WorkerNodeId = i.value.value)
                    select (Some w)
                    exactlyOneOrDefault
                }

            match x with
            | Some v -> v |> createWorkerNodeInfo p |> Ok
            | None -> UnableToLoadWorkerNodeErr i |> toError

        tryDbFun fromDbError g


    let private updateWorkerNodeRow (r : WorkerNodeEntity) (w : WorkerNodeInfo) =
        r.WorkerNodeName <- w.workerNodeName.value
        r.NumberOfCores <- w.noOfCores
        r.NodePriority <- w.nodePriority.value
        r.ModifiedOn <- DateTime.Now

        Ok()


    let private addWorkerNodeRow (ctx : DbContext) (w : WorkerNodeInfo) =
        let row = ctx.Dbo.WorkerNode.Create(
                            WorkerNodeId = w.workerNodeId.value.value,
                            WorkerNodeName = w.workerNodeName.value,
                            NodePriority = w.nodePriority.value,
                            NumberOfCores = w.noOfCores,
                            IsInactive = false,
                            CreatedOn = DateTime.Now,
                            ModifiedOn = DateTime.Now)

        row


    let upsertWorkerNodeInfo (w : WorkerNodeInfo) =
        let elevate e = e |> UpsertWorkerNodeInfoErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> UpsertWorkerNodeInfoDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for r in ctx.Dbo.WorkerNode do
                    where (r.WorkerNodeId = w.workerNodeId.value.value)
                    select (Some r)
                    exactlyOneOrDefault
                }

            let result =
                match x with
                | Some v -> updateWorkerNodeRow v w
                | None -> addWorkerNodeRow ctx w |> ignore; Ok()

            ctx.SubmitUpdates()
            result

        tryDbFun fromDbError g


    let private addWorkerNodeSolverRow (ctx : DbContext) (w : WorkerNodeId) (s : SolverId) =
        let row = ctx.Dbo.WorkerNodeSolver.Create(
                            WorkerNodeId = w.value.value,
                            SolverId = s.value,
                            CreatedOn = DateTime.Now,
                            IsDeployed = false)

        row


    let updateSolverDeploymentInfo (w : WorkerNodeId) (s : SolverId) r =
        let elevate e = e |> UpdateSolverDeploymentInfoErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> UpdateSolverDeploymentInfoDbErr |> elevate

        Logger.logInfo $"updateSolverDeploymentInfo: %A{w}, %A{r}."

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for r in ctx.Dbo.WorkerNodeSolver do
                    where (r.WorkerNodeId = w.value.value && r.SolverId = s.value)
                    select (Some r)
                    exactlyOneOrDefault
                }

            let x1 =
                match x with
                | Some v -> v
                | None -> addWorkerNodeSolverRow ctx w s

            match r with
            | Ok () ->
                x1.IsDeployed <- true
                x1.DeploymentError <- None
            | Error e ->
                x1.IsDeployed <- false
                x1.DeploymentError <- $"%A{e}" |> Some

            ctx.SubmitUpdates()
            Ok()

        tryDbFun fromDbError g


#endif

#if PARTITIONER_ADM

    /// Tries to reset RunQueue.
    let tryResetRunQueue (q : RunQueueId) =
        let elevate e = e |> TryResetRunQueueErr
        let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryResetRunQueueDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.TryResetRunQueue.Invoke q.value
            let m = r.ResultSet |> mapIntScalar

            match m with
            | Some 1 -> Ok ()
            | _ -> toError (ResetRunQueueEntryErr q)

        tryDbFun fromDbError g


    let tryUndeploySolver (solverId : SolverId) =
        let elevate e = e |> TryUndeploySolverErr
        //let toError e = e |> elevate |> Error
        let x e = CannotNotifyUndeploySolverErr e |> elevate
        let fromDbError e = e |> TryUndeploySolverDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.TryUndeploySolver.Invoke(``@solverId`` = solverId.value)
            let m = r.ResultSet |> mapIntScalar
            Logger.logTrace (fun () -> $"Updated {m} row(s) for solverId: '{solverId}'.")
            // r.ResultSet |> bindIntScalar x solverId
            Ok() // Ignore the result as we don't care about it here.

        tryDbFun fromDbError g


    let loadAllActiveSolverIds () =
        let elevate e = e |> LoadAllActiveSolverIdsErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> LoadAllActiveSolverIdsDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for s in ctx.Dbo.Solver do
                    where (s.IsInactive = false)
                    select s.SolverId
                }

            x
            |> Seq.toList
            |> List.map SolverId
            |> Ok

        tryDbFun fromDbError g


    let loadAllActiveWorkerNodeIds () =
        let elevate e = e |> LoadAllActiveWorkerNodeIdsErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> LoadAllActiveWorkerNodeIdsDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for w in ctx.Dbo.WorkerNode do
                    where (w.IsInactive = false)
                    select w.WorkerNodeId
                }

            x
            |> Seq.toList
            |> List.map (fun e -> e |> MessagingClientId |>  WorkerNodeId)
            |> Ok

        tryDbFun fromDbError g

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
                    join m in ctx.Dbo.ModelData on (q.RunQueueId = m.RunQueueId)
                    where (q.RunQueueId = i.value)
                    select (Some (m.ModelData, q.RunQueueStatusId, q.SolverId))
                    exactlyOneOrDefault
                }

            match x with
            | Some (v, s, q) ->
                let w() =
                    try
                        match RunQueueStatus.tryCreate s with
                        | Some st ->
                            match v |> tryDeserializeData<ModelBinaryData> with
                            | Ok b ->
                                match ModelData<'D>.tryFromModelBinaryData (SolverId q) b with
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
                | 1 -> Some RegularResultGeneration
                | 2 -> Some ForceResultGeneration
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


    /// Can modify progress related information when state is InProgress or CancelRequested.
    let tryUpdateProgress<'P> (q : RunQueueId) (pd : ProgressData<'P>) =
        let elevate e = e |> TryUpdateProgressErr
        //let toError e = e |> elevate |> Error
        let x e = CannotUpdateProgress e |> elevate
        let fromDbError e = e |> TryUpdateProgressDbErr |> elevate

        let g() =
            Logger.logTrace (fun () -> $"tryUpdateProgress: RunQueueId: {q}, progress data: %A{pd}.")
            let ctx = getDbContext getConnectionString
            let p = pd.toProgressData()

            let progressData =
                match p.progressDetailed with
                | Some d -> serializeProgress d
                | None -> null

            let r = ctx.Procedures.TryUpdateProgressRunQueue.Invoke(
                                        ``@runQueueId`` = q.value,
                                        ``@progress`` = p.progressInfo.progress,
                                        ``@evolutionTime`` = p.progressInfo.evolutionTime.value,
                                        ``@progressData`` = progressData,
                                        ``@callCount`` = p.progressInfo.callCount,
                                        ``@relativeInvariant`` = p.progressInfo.relativeInvariant.value)

            r.ResultSet |> bindIntScalar x q

        tryDbFun fromDbError g


    let tryGetSolverName (solverId : SolverId) =
        let elevate e = e |> TryGetSolverNameErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryGetSolverNameDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for s in ctx.Dbo.Solver do
                    where (s.SolverId = solverId.value)
                    select (Some s.SolverName)
                    exactlyOneOrDefault
                }

            x |> Option.map SolverName |> Ok

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
                    join s in ctx.Dbo.Solver on (q.SolverId = s.SolverId)
                    where (s.IsDeployed = false && (q.RunQueueStatusId = RunQueueStatus.InProgressRunQueue.value || q.RunQueueStatusId = RunQueueStatus.CancelRequestedRunQueue.value))
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


    // let tryPickRunQueue () =
    //     let elevate e = e |> TryPickRunQueueErr
    //     //let toError e = e |> elevate |> Error
    //     let fromDbError e = e |> TryPickRunQueueDbErr |> elevate
    //
    //     let g() =
    //         let ctx = getDbContext getConnectionString
    //
    //         let x =
    //             query {
    //                 for q in ctx.Dbo.RunQueue do
    //                 where (q.RunQueueStatusId = RunQueueStatus.NotStartedRunQueue.value && q.RetryCount <= q.MaxRetries)
    //                 sortBy q.RunQueueOrder
    //                 select (Some q.RunQueueId)
    //                 exactlyOneOrDefault
    //             }
    //
    //         x |> Option.map RunQueueId |> Ok
    //
    //     tryDbFun fromDbError g
    //
    //
    // let tryPickRunQueue (lastErrMinAgo : int) =
    //     let elevate e = e |> TryPickRunQueueErr
    //     let fromDbError e = e |> TryPickRunQueueDbErr |> elevate
    //
    //     let g() =
    //         let ctx = getDbContext getConnectionString
    //
    //         let x =
    //             query {
    //                 for q in ctx.Dbo.RunQueue do
    //                 where (
    //                     q.RunQueueStatusId = RunQueueStatus.NotStartedRunQueue.value &&
    //                     q.RetryCount <= q.MaxRetries &&
    //                     (q.LastErrorOn = None || q.LastErrorOn.Value < DateTime.Now.AddMinutes(-float lastErrMinAgo))
    //                 )
    //                 sortBy q.RunQueueOrder
    //                 select (Some q.RunQueueId)
    //                 exactlyOneOrDefault
    //             }
    //
    //         x |> Option.map RunQueueId |> Ok
    //
    //     tryDbFun fromDbError g


    // let loadAllActiveRunQueueId (lastErrMinAgo : int) =
    //     let elevate e = e |> LoadAllActiveRunQueueIdErr
    //     //let toError e = e |> elevate |> Error
    //     let fromDbError e = e |> LoadAllActiveRunQueueIdDbErr |> elevate
    //
    //     let g() =
    //         let ctx = getDbContext getConnectionString
    //
    //         let x =
    //             query {
    //                 for q in ctx.Dbo.RunQueue do
    //                 where ((q.RunQueueStatusId = RunQueueStatus.NotStartedRunQueue.value || q.RunQueueStatusId = RunQueueStatus.InProgressRunQueue.value  || q.RunQueueStatusId = RunQueueStatus.CancelRequestedRunQueue.value)
    //                        && q.RetryCount <= q.MaxRetries && (q.LastErrorOn = None || q.LastErrorOn.Value < DateTime.Now.AddMinutes(- float lastErrMinAgo)))
    //                 select q.RunQueueId
    //             }
    //
    //         x
    //         |> Seq.toList
    //         |> List.map RunQueueId
    //         |> Ok
    //
    //     tryDbFun fromDbError g


    let loadAllNotStartedRunQueueId (lastErrMinAgo : float<minute>) =
        let elevate e = e |> LoadAllNotStartedRunQueueIdErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> LoadAllNotStartedRunQueueIdDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for q in ctx.Dbo.RunQueue do
                    where (q.RunQueueStatusId = RunQueueStatus.NotStartedRunQueue.value && q.RetryCount <= q.MaxRetries && (q.LastErrorOn = None || q.LastErrorOn.Value < DateTime.Now.AddMinutes(- float lastErrMinAgo)))
                    select q.RunQueueId
                }

            x
            |> Seq.toList
            |> List.map RunQueueId
            |> Ok

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
    let tryNotifyRunQueue (q : RunQueueId) (r : ResultNotificationType option) =
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


    let setSolverDeployed (solverId : SolverId) =
        let elevate e = e |> SetSolverDeployedErr
        let fromDbError e = e |> SetSolverDeployedDbErr |> elevate

        let g() =
            processSolver
                solverId
                (fun ctx s ->
                    s.IsDeployed <- true
                    ctx.SubmitUpdates()
                    Ok())
                (fun _ -> Error (SolverNotFound solverId))

        tryDbFun fromDbError g


    let loadAllNotDeployedSolverId () =
        let elevate e = e |> LoadAllNotDeployedSolverIdErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> LoadAllNotDeployedSolverIdDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for s in ctx.Dbo.Solver do
                    where (s.IsDeployed = false)
                    select s.SolverId
                }

            x
            |> Seq.toList
            |> List.map SolverId
            |> Ok

        tryDbFun fromDbError g


    let tryUpdateFailedSolver (q : RunQueueId) e =
        let elevate e = e |> TryUpdateFailedSolverErr
        //let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryUpdateFailedSolverDbErr |> elevate

        let g() =
            processRunQueue
                q
                (fun ctx s ->
                    let x =
                        match s.RetryCount < s.MaxRetries with
                        | true -> CanRetry
                        | false -> ExceededRetryCount { retryCount = s.RetryCount; maxRetries = s.MaxRetries }

                    s.RetryCount <- (s.RetryCount + 1)
                    s.LastErrorOn <- (Some DateTime.Now)
                    s.ErrorMessage <- (Some $"%A{e}")
                    ctx.SubmitUpdates()
                    Ok x)
                (fun _ -> Error (q |> TryUpdateFailedSolverNoRunQueueErr |> TryUpdateFailedSolverErr))

        tryDbFun fromDbError g


    let tryGetWorkerNodeReinstallationInfo() =
        match tryLoadSetting workerNodeReinstallationSettingName with
        | Ok (Some s) ->
            match s.settingLong with
            | Some v -> int v |> BuildNumber |> Some |> Ok
            | None -> Ok None
        | Ok None -> Ok None
        | Error e -> Error e


    let tryDeleteWorkerNodeReinstallationInfo() =
        match tryDeleteSetting workerNodeReinstallationSettingName with
        | Ok _ -> Ok()
        | Error e -> Error e


    let trySaveWorkerNodeReinstallationInfo (BuildNumber buildNumber) =
        let s =
            {
                settingName = workerNodeReinstallationSettingName
                settingBool = None
                settingGuid = None
                settingLong = Some (int64 buildNumber)
                settingText = None
                settingBinary = None
                createdOn = DateTime.Now
            }

        match tryDeleteWorkerNodeReinstallationInfo() with
        | Ok _ -> trySaveSetting s
        | Error e -> Error e

#endif
