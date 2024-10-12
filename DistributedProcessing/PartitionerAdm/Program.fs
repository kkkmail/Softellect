namespace Softellect.DistributedProcessing.PartitionerAdm

open Argu
open System
open Softellect.DistributedProcessing.PartitionerAdm.CommandLine
open Softellect.DistributedProcessing.PartitionerAdm.Implementation

module Program =

    let partitionerAdmMain programName argv =
        let parser = ArgumentParser.Create<PartitionerAdmArgs>(programName = programName)
        let ctx = PartitionerAdmContext.create()
        let results = (parser.Parse argv).GetAllResults()

        let retVal =
            results
            |> List.map (fun r ->
                    match r with
                    | AddSolver addSolverArgs -> addSolver ctx (addSolverArgs.GetAllResults())
                    | SendSolver sendSolverArgs -> sendSolver ctx (sendSolverArgs.GetAllResults())
                )

        retVal |> List.map (fun x -> printfn $"%A{x}") |> ignore
        0


        //results
        //|> List.iter (fun r ->
        //        match r with
        //        | AddSolver solverArgs ->
        //            let r = solverArgs.GetAllResults()

        //            r
        //            |> List.iter (fun x ->
        //                    match x with
        //                    | SolverArgs.Id id -> printfn $"SolverId: '{id}'."
        //                    | Name name -> printfn $"Name: '{name}'."
        //                    | Folder folder -> printfn $"Folder: '{folder}'."
        //                    | Description description -> printfn $"Description: '{description}'."
        //                )

        //            let result = addSolver proxy r
        //            printfn $"%A{result}"
        //        | Test testArgs ->
        //            let r = testArgs.GetAllResults()

        //            r
        //            |> List.iter (fun x ->
        //                    match x with
        //                    | TestArgs.Id id -> printfn $"Id: {id}."
        //                )
        //    )
        //|> ignore

        //0
