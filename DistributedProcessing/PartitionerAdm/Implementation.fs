namespace Softellect.DistributedProcessing.PartitionerAdm

open Softellect.DistributedProcessing.PartitionerAdm.CommandLine
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Messaging.Primitives
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

    type PartitionerAdmProxy =
        {
            saveSolver : Solver -> DistributedProcessingUnitResult
            tryLoadSolver : SolverId -> DistributedProcessingResult<Solver>
        }

        static member create () =
            {
                saveSolver = saveSolver
                tryLoadSolver = tryLoadSolver
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
                    partitionerAdmProxy = PartitionerAdmProxy.create()
                    partitionerInfo = w
                }
            | Error e -> failwith $"ERROR: {e}"


    let private sendSolverImpl (solver : Solver) (p : PartitionerId) w =
        printfn $"sendSolver: {solver.solverName}."

        let result =
            {
                workerNodeRecipient = w
                deliveryType = GuaranteedDelivery
                messageData = UpdateSolverWrkMsg solver
            }.getMessageInfo()
            |> createMessage messagingDataVersion p.messagingClientId
            |> saveMessage<DistributedProcessingMessageData> messagingDataVersion

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
                match sendSolverImpl solver ctx.partitionerInfo.partitionerId w with
                | Ok () ->
                    printfn $"sendSolver: solver with id '{s}' was sent to worker node '{w}'."
                    Ok ()
                | Error e -> (s, w, e) |> UnableToSendSolverErr |> SendSolverErr |> Error
            | Error e ->
                printfn $"sendSolver: error: {e}."
                Error e
        | _ -> failwith "sendSolver: Invalid arguments."
