namespace Softellect.DistributedProcessing.PartitionerAdm

open Softellect.DistributedProcessing.PartitionerAdm.CommandLine
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Messaging.Client
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.DataAccess.PartitionerAdm
open Softellect.DistributedProcessing.VersionInfo

module Implementation =

    type PartitionerAdmProxy =
        {
            saveSolver : Solver -> DistributedProcessingUnitResult
        }

        static member create () =
            {
                saveSolver = saveSolver
            }


    //let sendSolver proxy (solver : Solver) m =
    //    printfn $"sendSolver: {solver.solverName}."
    //    let message = createMessage messagingDataVersion m solver
    //    Ok()


    let addSolver (proxy : PartitionerAdmProxy) (x : list<SolverArgs>) =
        let so = x |> List.tryPick (fun e -> match e with | Id id -> SolverId id |> Some | _ -> None)
        let no = x |> List.tryPick (fun e -> match e with | Name name -> SolverName name |> Some | _ -> None)
        let fo = x |> List.tryPick (fun e -> match e with | Folder folder -> FolderName folder |> Some | _ -> None)
        let de = x |> List.tryPick (fun e -> match e with | Description description -> description |> Some | _ -> None)

        match (so, no, fo) with
        | (Some s, Some n, Some f) ->
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
                proxy.saveSolver solver
            | Error e ->
                printfn $"Error: {e}."
                UnableToZipSolverErr (s, f, e) |> SaveSolverErr |> Error
        | _ -> failwith "Invalid arguments."
