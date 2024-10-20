namespace Softellect.DistributedProcessing.PartitionerAdm

open Softellect.DistributedProcessing.PartitionerAdm.CommandLine
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.Primitives.PartitionerAdm
open Softellect.Messaging.Errors
open Softellect.Messaging.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Sys
open Softellect.Sys.AppSettings
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Messaging.Client
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.DataAccess.PartitionerAdm
open Softellect.DistributedProcessing.VersionInfo
open Softellect.Messaging.DataAccess
open Softellect.DistributedProcessing.AppSettings.PartitionerAdm

module Implementation =

    let private toError g f = f |> g |> Error
    let private addError g f e = ((f |> g) + e) |> Error


    type PartitionerAdmProxy =
        {
            saveSolver : Solver -> DistributedProcessingUnitResult
            tryLoadSolver : SolverId -> DistributedProcessingResult<Solver>
            tryLoadRunQueue : RunQueueId -> DistributedProcessingResult<RunQueue option>
            upsertRunQueue : RunQueue -> DistributedProcessingUnitResult
            createMessage : MessageInfo<DistributedProcessingMessageData> -> Message<DistributedProcessingMessageData>
            saveMessage : Message<DistributedProcessingMessageData> -> MessagingUnitResult
            tryResetRunQueue : RunQueueId -> DistributedProcessingUnitResult
        }

        static member create (p : PartitionerId) =
            {
                saveSolver = saveSolver
                tryLoadSolver = tryLoadSolver
                tryLoadRunQueue = tryLoadRunQueue
                upsertRunQueue = upsertRunQueue
                createMessage = createMessage messagingDataVersion p.messagingClientId
                saveMessage = saveMessage<DistributedProcessingMessageData> messagingDataVersion
                tryResetRunQueue = tryResetRunQueue
            }


    type PartitionerAdmContext =
        {
            partitionerAdmProxy : PartitionerAdmProxy
            partitionerInfo : PartitionerInfo
        }

        static member create () =
            let providerRes = AppSettingsProvider.tryCreate AppSettingsFile

            match providerRes with
            | Ok provider ->
                let w = loadPartitionerInfo provider

                {
                    partitionerAdmProxy = PartitionerAdmProxy.create w.partitionerId
                    partitionerInfo = w
                }
            | Error e -> failwith $"ERROR: {e}"


    let private sendSolverImpl (ctx : PartitionerAdmContext) (solver : Solver) w =
        printfn $"sendSolver: {solver.solverName}."

        let result =
            {
                workerNodeRecipient = w
                deliveryType = GuaranteedDelivery
                messageData = UpdateSolverWrkMsg solver
            }.getMessageInfo()
            |> ctx.partitionerAdmProxy.createMessage
            |> ctx.partitionerAdmProxy.saveMessage

        result


    let addSolver (ctx : PartitionerAdmContext) (x : list<AddSolverArgs>) =
        let so = x |> List.tryPick (fun e -> match e with | AddSolverArgs.SolverId id -> SolverId id |> Some | _ -> None)
        let no = x |> List.tryPick (fun e -> match e with | Name name -> SolverName name |> Some | _ -> None)
        let fo = x |> List.tryPick (fun e -> match e with | Folder folder -> FolderName folder |> Some | _ -> None)
        let de = x |> List.tryPick (fun e -> match e with | Description description -> description |> Some | _ -> None)

        match (so, no, fo) with
        | Some s, Some n, Some f ->
            match zipFolder f with
            | Ok d ->
                let solver =
                    {
                        solverId = s
                        solverName = n
                        solverData = Some d
                        description = de
                    }

                printfn $"Solver with id '{s}', name '{n}', and folder '{f}' was added. Solver size: {(solver.solverData |> Option.map (fun e -> e.Length) |> Option.defaultValue 0):N0}"
                ctx.partitionerAdmProxy.saveSolver solver
            | Error e ->
                printfn $"Error: {e}."
                UnableToZipSolverErr (s, f, e) |> SaveSolverErr |> Error
        | _ -> failwith "addSolver: Invalid arguments."


    let sendSolver (ctx : PartitionerAdmContext) (x : list<SendSolverArgs>) =
        let so = x |> List.tryPick (fun e -> match e with | SendSolverArgs.SolverId id -> SolverId id |> Some | _ -> None)
        let wo = x |> List.tryPick (fun e -> match e with | SendSolverArgs.WorkerNodeId id -> id |> MessagingClientId |> WorkerNodeId |> Some | _ -> None)

        match (so, wo) with
        | Some s, Some w ->
            printfn $"sendSolver: solver with id '{s}' is being sent to worker node '{w}'."

            match ctx.partitionerAdmProxy.tryLoadSolver s with
            | Ok solver ->
                match sendSolverImpl ctx solver w with
                | Ok () ->
                    printfn $"sendSolver: solver with id '{s}' was sent to worker node '{w}'."
                    Ok ()
                | Error e -> (s, w, e) |> UnableToSendSolverErr |> SendSolverErr |> Error
            | Error e ->
                printfn $"sendSolver: error: {e}."
                Error e
        | _ -> failwith "sendSolver: Invalid arguments."



    let tryCancelRunQueue (ctx : PartitionerAdmContext) q c =
        let addError = addError TryCancelRunQueueRunnerErr
        let toError = toError TryCancelRunQueueRunnerErr

        printfn $"tryCancelRunQueue: runQueueId: '%A{q}', c: '%A{c}'."

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

                        messageData = (q, c) |> RequestChartsWrkMsg |> WorkerNodeMsg |> UserMsg
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
        match tryGetRunQueueIdToModify x with
        | Some q ->
            let n = getResultNotificationTypeOpt x
            let r = getResetIfFailed x

            match getCancellationTypeOpt x with
            | Some c -> tryCancelRunQueue ctx q c
            | None ->
                match r with
                | true -> tryResetIfFailed ctx q
                | false ->
                    match n with
                    | Some v -> tryRequestResults ctx q v
                    | None -> Ok ()
        | None ->
            printfn $"modifyRunQueue: No runQueueId to modify found."
            Ok ()
