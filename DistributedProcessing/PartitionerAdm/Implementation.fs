namespace Softellect.DistributedProcessing.PartitionerAdm

open Softellect.DistributedProcessing.PartitionerAdm.CommandLine
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.Primitives.PartitionerAdm
open Softellect.Messaging.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Client
open Softellect.Messaging.DataAccess
open Softellect.Sys
open Softellect.Sys.AppSettings
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.Sys.Logging
open Softellect.Sys.Crypto
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.Messages
open Softellect.DistributedProcessing.DataAccess.PartitionerAdm
open Softellect.DistributedProcessing.VersionInfo
open Softellect.DistributedProcessing.AppSettings.PartitionerAdm
open Softellect.Sys.Rop

module Implementation =

    let private toError g f = f |> g |> Error
    let private addError g f e = ((f |> g) + e) |> Error
    let private foldUnitResults = foldUnitResults DistributedProcessingError.addError
    let private combineUnitResults = combineUnitResults DistributedProcessingError.addError


    /// Default implementation of solver encryption.
    let private tryEncryptSolver (i : PartitionerInfo) (solver : Solver) (w : WorkerNodeId) : DistributedProcessingResult<EncryptedSolver> =
        Logger.logInfo $"tryEncryptSolver: %A{solver.solverId}, %A{solver.solverName}, %A{w}"
        match tryLoadPartitionerPrivateKey(), tryLoadWorkerNodePublicKey w, trySerialize solverSerializationFormat solver with
        | Ok (Some p1), Ok (Some w1), Ok data ->
            Logger.logInfo $"tryEncryptSolver: encrypting - {data.Length:N0} bytes."

            match tryEncryptAndSign i.solverEncryptionType data p1 w1 with
            | Ok r ->
                Logger.logInfo $"tryEncryptSolver: encrypted - {r.Length:N0} bytes."
                r |> EncryptedSolver |> Ok
            | Error e -> e |> TryEncryptSolverSysErr |> TryEncryptSolverErr |> Error
        | _ -> (w, solver.solverId) |> TryEncryptSolverCriticalErr |> TryEncryptSolverErr |> Error


    let private tryGeneratePartitionerKeys (p : PartitionerId) force =
        let g() =
            let publicKey, privateKey = generateKey (KeyId p.value)

            match trySavePartitionerPrivateKey privateKey, trySavePartitionerPublicKey publicKey with
            | Ok(), Ok() -> Ok()
            | Error e, Ok() -> Error e
            | Ok(), Error e -> Error e
            | Error e1, Error e2 -> e1 + e2 |> Error

        match tryLoadPartitionerPrivateKey (), tryLoadPartitionerPublicKey (), force with
        | Ok (Some _), Ok (Some _), false -> Ok()
        | _ -> g()


    let private tryExportPartitionerPublicKey (folderName : FolderName) overwrite =
        let toError e = e |> TryLoadPartitionerPublicKeyErr |> Error
        match tryLoadPartitionerPublicKey() with
        | Ok (Some key) ->
            match tryExportPublicKey folderName key overwrite with
            | Ok() -> Ok()
            | Error e -> e |> TryExportPartitionerPublicKeyErr |> toError
        | Ok None -> NoPartitionerPublicKeyErr |> toError
        | Error e -> Error e


    let private tryImportWorkerNodePublicKey (fileName : FileName) =
        match tryImportPublicKey fileName None with
        | Ok key -> Ok key
        | Error e -> e |> TryImportWorkerNodePublicKeyErr |> TryLoadWorkerNodePublicKeyErr |> Error


    type PartitionerAdmProxy =
        {
            getSolverHash : SolverId -> DistributedProcessingResult<Sha256Hash option>
            saveSolver : Solver -> DistributedProcessingUnitResult
            checkIfSolverDeployed : SolverId -> WorkerNodeId -> DistributedProcessingResult<bool>
            tryUndeploySolver : SolverId -> DistributedProcessingUnitResult
            tryLoadSolver : SolverId -> DistributedProcessingResult<Solver>
            tryEncryptSolver : Solver -> WorkerNodeId -> DistributedProcessingResult<EncryptedSolver>
            tryGeneratePartitionerKeys : bool -> DistributedProcessingUnitResult
            tryExportPublicKey : FolderName -> bool -> DistributedProcessingUnitResult
            tryImportWorkerNodePublicKey : FileName -> DistributedProcessingResult<KeyId * PublicKey>
            tryUpdateWorkerNodePublicKey : WorkerNodeId -> PublicKey -> DistributedProcessingUnitResult
            tryLoadRunQueue : RunQueueId -> DistributedProcessingResult<RunQueue option>
            upsertRunQueue : RunQueue -> DistributedProcessingUnitResult
            createMessage : MessageInfo<DistributedProcessingMessageData> -> Message<DistributedProcessingMessageData>
            saveMessage : Message<DistributedProcessingMessageData> -> MessagingUnitResult
            tryResetRunQueue : RunQueueId -> DistributedProcessingUnitResult
            loadAllActiveSolverIds : unit -> DistributedProcessingResult<List<SolverId>>
            loadAllActiveWorkerNodeIds : unit -> DistributedProcessingResult<List<WorkerNodeId>>
        }

        static member create (i : PartitionerInfo) =
            {
                getSolverHash = getSolverHash
                saveSolver = saveSolver
                checkIfSolverDeployed = checkIfSolverDeployed
                tryUndeploySolver = tryUndeploySolver
                tryLoadSolver = tryLoadSolver
                tryEncryptSolver = tryEncryptSolver i
                tryGeneratePartitionerKeys = tryGeneratePartitionerKeys i.partitionerId
                tryExportPublicKey = tryExportPartitionerPublicKey
                tryImportWorkerNodePublicKey = tryImportWorkerNodePublicKey
                tryUpdateWorkerNodePublicKey = tryUpdateWorkerNodePublicKey
                tryLoadRunQueue = tryLoadRunQueue
                upsertRunQueue = upsertRunQueue
                createMessage = createMessage messagingDataVersion i.partitionerId.messagingClientId
                saveMessage = saveMessage<DistributedProcessingMessageData> messagingDataVersion
                tryResetRunQueue = tryResetRunQueue
                loadAllActiveSolverIds = loadAllActiveSolverIds
                loadAllActiveWorkerNodeIds = loadAllActiveWorkerNodeIds
            }


    type PartitionerAdmContext =
        {
            partitionerAdmProxy : PartitionerAdmProxy
            partitionerInfo : PartitionerInfo
        }

        static member create () =
            match AppSettingsProvider.tryCreate() with
            | Ok provider ->
                let w = loadPartitionerInfo provider

                {
                    partitionerAdmProxy = PartitionerAdmProxy.create w
                    partitionerInfo = w
                }
            | Error e -> failwith $"ERROR: {e}"


    let addSolver (ctx : PartitionerAdmContext) (x : list<AddSolverArgs>) =
        let so = x |> List.tryPick (fun e -> match e with | AddSolverArgs.SolverId id -> SolverId id |> Some | _ -> None)
        let no = x |> List.tryPick (fun e -> match e with | Name name -> SolverName name |> Some | _ -> None)
        let fo = x |> List.tryPick (fun e -> match e with | AddSolverArgs.Folder folder -> FolderName folder |> Some | _ -> None)
        let de = x |> List.tryPick (fun e -> match e with | Description description -> description |> Some | _ -> None)
        let force = x |> List.tryPick (fun e -> match e with | AddSolverArgs.Force e -> Some e | _ -> None) |> Option.defaultValue false

        match (so, no, fo) with
        | Some s, Some n, Some f ->
            match zipFolder f with
            | Ok d ->
                let hash = calculateSha256Hash d

                let doAddSolver() =
                    let solver =
                        {
                            solverId = s
                            solverName = n
                            solverData = d |> SolverData |> Some
                            solverHash = hash
                            description = de
                        }

                    Logger.logInfo $"Solver with id '{s}', name '{n}', folder '{f}', and hash '{hash}' was added. Solver size: {(solver.solverData |> Option.map _.value.Length |> Option.defaultValue 0):N0}"
                    let r1 = ctx.partitionerAdmProxy.saveSolver solver
                    let r2 = ctx.partitionerAdmProxy.tryUndeploySolver solver.solverId
                    let r = combineUnitResults r1 r2
                    r

                let r =
                    match ctx.partitionerAdmProxy.getSolverHash s with
                    | Ok (Some h) ->
                        match h = hash, force with
                        | true, true ->
                            Logger.logInfo $"Solver with %A{s} with hash: '{hash}' is forced through."
                            doAddSolver()
                        | true, false ->
                            Logger.logInfo $"Solver with %A{s} with hash: '{hash}' was already added. Pass '-f true' to force it through."
                            Ok()
                        | _  -> doAddSolver()
                    | Ok None -> doAddSolver()
                    | Error e -> combineUnitResults (Error e) (doAddSolver())

                Logger.logIfError r
            | Error e ->
                Logger.logError $"Error: {e}."
                UnableToZipSolverErr (s, f, e) |> SaveSolverErr |> Error
        | _ ->
            let m = $"Some of the arguments are invalid: %A{so}, %A{no}, %A{fo}."
            Logger.logCrit m
            failwith $"addSolver: {m}."


    let private sendSolverImpl (ctx : PartitionerAdmContext) force s w =
        Logger.logInfo $"Solver with id '{s}' is being sent to worker node '%A{w}', force: {force}."

        match ctx.partitionerAdmProxy.checkIfSolverDeployed s w, force with
        | Ok true, true | Ok false, _ ->
            match ctx.partitionerAdmProxy.tryLoadSolver s with
            | Ok solver ->
                match ctx.partitionerAdmProxy.tryEncryptSolver solver w with
                | Ok encryptedSolver ->
                    let result =
                        {
                            workerNodeRecipient = w
                            deliveryType = GuaranteedDelivery
                            messageData = UpdateSolverWrkMsg encryptedSolver
                        }.getMessageInfo()
                        |> ctx.partitionerAdmProxy.createMessage
                        |> ctx.partitionerAdmProxy.saveMessage

                    match result with
                    | Ok () ->
                        Logger.logInfo $"Solver with id '%A{s}' was sent to worker node '%A{w}'."
                        Ok ()
                    | Error e -> (s, w, e) |> UnableToSendSolverErr |> SendSolverErr |> Error
                | Error e ->
                    Logger.logError $"Unable to encrypt solver '%A{s}' for worker node '%A{w}, error: %A{e}."
                    Error e
            | Error e ->
                Logger.logError $"Unable to load solver '%A{s}' for worker node '%A{w}, error: %A{e}."
                Error e
        | Ok true, false ->
            Logger.logInfo $"Solver '%A{s}' was already deployed to worker node '%A{w}. Pass '-f true' to force it through."
            Ok()
        | Error e, _ ->
            Logger.logError $"Unable to check if the solver '%A{s}' is deployed to worker node '%A{w}, error: %A{e}."
            Error e


    let sendSolver (ctx : PartitionerAdmContext) (x : list<SendSolverArgs>) =
        let so = x |> List.tryPick (fun e -> match e with | SendSolverArgs.SolverId id -> SolverId id |> Some | _ -> None)
        let wo = x |> List.tryPick (fun e -> match e with | SendSolverArgs.WorkerNodeId id -> id |> MessagingClientId |> WorkerNodeId |> Some | _ -> None)
        let force = x |> List.tryPick (fun e -> match e with | SendSolverArgs.Force e -> Some e | _ -> None) |> Option.defaultValue false

        match (so, wo) with
        | Some s, Some w -> sendSolverImpl ctx force s w
        | _ ->
            let m = $"Some of the arguments are invalid: %A{so}, %A{wo}."
            Logger.logCrit m
            failwith $"sendSolver: {m}."


    let sendAllSolvers (ctx : PartitionerAdmContext) (x : list<SendAllSolversArgs>) =
        let force = x |> List.tryPick (fun e -> match e with | SendAllSolversArgs.Force e -> Some e | _ -> None) |> Option.defaultValue false

        let sendAll wl s =
            wl
            |> List.map (sendSolverImpl ctx force s)
            |> foldUnitResults

        match ctx.partitionerAdmProxy.loadAllActiveSolverIds(), ctx.partitionerAdmProxy.loadAllActiveWorkerNodeIds() with
        | Ok sl, Ok wl ->
            let r =
                sl
                |> List.map (sendAll wl)
                |> foldUnitResults

            r
        | Error e, Ok _ -> Error e
        | Ok _, Error e -> Error e
        | Error e1, Error e2 -> e1 + e2 |> Error


    let addWorkerNodeService (ctx : PartitionerAdmContext) (x : list<AddWorkerNodeServiceArgs>) =
        // Map the worker node service args to solver args
        let solverArgs =
            [
                // Use the predefined worker node service ID
                AddSolverArgs.SolverId(SolverId.workerNodeServiceId.value)

                // Use the predefined worker node service name
                AddSolverArgs.Name(SolverName.workerNodeServiceName.value)

                // Map the folder parameter
                match x |> List.tryPick (fun e -> match e with | AddWorkerNodeServiceArgs.Folder folder -> Some folder | _ -> None) with
                | Some folder -> AddSolverArgs.Folder(folder)
                | None -> failwith "Folder parameter is required for adding worker node service."

                // Map the force parameter if it exists
                match x |> List.tryPick (fun e -> match e with | AddWorkerNodeServiceArgs.Force force -> Some force | _ -> None) with
                | Some force -> AddSolverArgs.Force(force)
                | None -> AddSolverArgs.Force(false) // Default to false if not provided
            ]

        // Log that we're adding a worker node service
        Logger.logInfo $"""Adding worker node service from folder '{solverArgs |> List.tryPick (fun e -> match e with | AddSolverArgs.Folder f -> Some f | _ -> None) |> Option.defaultValue "unknown"}'"""

        // Call the existing addSolver function with our mapped parameters
        addSolver ctx solverArgs


    let sendWorkerNodeService (ctx : PartitionerAdmContext) (x : list<SendWorkerNodeServiceArgs>) =
        // Map the worker node service args to solver args
        let solverArgs =
            [
                // Use the predefined worker node service ID
                SendSolverArgs.SolverId(SolverId.workerNodeServiceId.value)

                // Map the worker node ID parameter
                match x |> List.tryPick (fun e -> match e with | SendWorkerNodeServiceArgs.WorkerNodeId id -> Some id | _ -> None) with
                | Some id -> SendSolverArgs.WorkerNodeId(id)
                | None -> failwith "WorkerNodeId parameter is required for sending worker node service."

                // Map the force parameter if it exists
                match x |> List.tryPick (fun e -> match e with | SendWorkerNodeServiceArgs.Force force -> Some force | _ -> None) with
                | Some force -> SendSolverArgs.Force(force)
                | None -> SendSolverArgs.Force(false) // Default to false if not provided
            ]

        // Log that we're sending a worker node service
        let workerNodeId = x |> List.tryPick (fun e -> match e with | SendWorkerNodeServiceArgs.WorkerNodeId id -> Some id | _ -> None)
        Logger.logInfo $"Sending worker node service to worker node with ID: %A{workerNodeId}"

        // Call the existing sendSolver function with our mapped parameters
        sendSolver ctx solverArgs


    let tryCancelRunQueue (ctx : PartitionerAdmContext) q c =
        let addError = addError TryCancelRunQueueRunnerErr
        let toError = toError TryCancelRunQueueRunnerErr

        Logger.logTrace $"tryCancelRunQueue: runQueueId: '%A{q}', c: '%A{c}'."

        match ctx.partitionerAdmProxy.tryLoadRunQueue q with
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
                        |> ctx.partitionerAdmProxy.createMessage
                        |> ctx.partitionerAdmProxy.saveMessage

                    match r11 with
                    | Ok v -> Ok v
                    | Error e -> TryCancelRunQueueRunnerError.MessagingTryCancelRunQueueRunnerErr e |> toError
                | None -> Ok()

            let r2 =
                match r.runQueueStatus with
                | NotStartedRunQueue -> { r with runQueueStatus = CancelledRunQueue } |> ctx.partitionerAdmProxy.upsertRunQueue
                | RunRequestedRunQueue -> { r with runQueueStatus = CancelRequestedRunQueue } |> ctx.partitionerAdmProxy.upsertRunQueue
                | InProgressRunQueue -> { r with runQueueStatus = CancelRequestedRunQueue } |> ctx.partitionerAdmProxy.upsertRunQueue
                | CancelRequestedRunQueue -> { r with runQueueStatus = CancelRequestedRunQueue } |> ctx.partitionerAdmProxy.upsertRunQueue
                | _ -> q |> TryCancelRunQueueRunnerError.InvalidRunQueueStatusRunnerErr |> toError

            Rop.combineUnitResults (+) r1 r2
        | Ok None -> toError (TryCancelRunQueueRunnerError.TryLoadRunQueueRunnerErr q)
        | Error e -> addError (TryCancelRunQueueRunnerError.TryLoadRunQueueRunnerErr q) e


    let tryRequestResults (ctx : PartitionerAdmContext) q c =
        let addError = addError TryRequestResultsRunnerErr
        let toError = toError TryRequestResultsRunnerErr

        match ctx.partitionerAdmProxy.tryLoadRunQueue q with
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

                        messageData = (q, c) |> RequestResultsWrkMsg |> WorkerNodeMsg |> UserMsg
                    }
                    |> ctx.partitionerAdmProxy.createMessage
                    |> ctx.partitionerAdmProxy.saveMessage

                match r1 with
                | Ok v -> Ok v
                | Error e -> MessagingTryRequestResultsRunnerErr e |> toError
            | None -> Ok()
        | Ok None -> toError (TryRequestResultsRunnerError.TryLoadRunQueueRunnerErr q)
        | Error e -> addError (TryRequestResultsRunnerError.TryLoadRunQueueRunnerErr q) e


    let tryResetIfFailed (ctx : PartitionerAdmContext) q =
        ctx.partitionerAdmProxy.tryResetRunQueue q


    let modifyRunQueue (ctx : PartitionerAdmContext) (x : list<ModifyRunQueueArgs>) =
        match x |> List.tryPick (fun e -> match e with | RunQueueIdToModify e -> e |> RunQueueId |> Some | _ -> None) with
        | Some q ->
            let n = x |> List.tryPick (fun e -> match e with | ReportResults e -> (match e with | false -> RegularResultGeneration | true -> ForceResultGeneration) |> Some | _ -> None)
            let r = x |> List.tryPick (fun e -> match e with | ResetIfFailed -> Some true | _ -> None) |> Option.defaultValue false

            match x |> List.tryPick (fun e -> match e with | CancelOrAbort e -> (match e with | false -> (CancelWithResults None) | true -> (AbortCalculation None)) |> Some | _ -> None) with
            | Some c -> tryCancelRunQueue ctx q c
            | None ->
                match r with
                | true -> tryResetIfFailed ctx q
                | false ->
                    match n with
                    | Some v -> tryRequestResults ctx q v
                    | None -> Ok ()
        | None ->
            Logger.logError $"modifyRunQueue: No runQueueId to modify found."
            Ok ()


    let generateKeys (ctx : PartitionerAdmContext) (x : list<GenerateKeysArgs>) =
        let force = x |> List.tryPick (fun e -> match e with | GenerateKeysArgs.Force e -> Some e | _ -> None) |> Option.defaultValue false
        let result = ctx.partitionerAdmProxy.tryGeneratePartitionerKeys force
        result


    let exportPublicKey (ctx : PartitionerAdmContext) (x : list<ExportPublicKeyArgs>) =
        let ofn = x |> List.tryPick (fun e -> match e with | OutputFolderName e -> e |> FolderName |> Some | _ -> None)
        let o = x |> List.tryPick (fun e -> match e with | Overwrite e -> e |> Some | _ -> None) |> Option.defaultValue false

        match ofn with
        | Some f -> ctx.partitionerAdmProxy.tryExportPublicKey f o
        | None ->
            Logger.logError "exportPublicKey - output folder name was not provided."
            Ok()


    let importPublicKey (ctx : PartitionerAdmContext) (x : list<ImportPublicKeyArgs>) =
        let ifn = x |> List.tryPick (fun e -> match e with | InputFileName e -> e |> FileName |> Some | _ -> None)

        match ifn with
        | Some f ->
            match ctx.partitionerAdmProxy.tryImportWorkerNodePublicKey f with
            | Ok (k, key) ->
                let w = k.value |> MessagingClientId |> WorkerNodeId
                ctx.partitionerAdmProxy.tryUpdateWorkerNodePublicKey w key
            | Error e -> Error e
        | None ->
            Logger.logError "importPublicKey - input file name was not provided."
            Ok()
