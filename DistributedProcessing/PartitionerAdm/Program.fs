namespace Softellect.DistributedProcessing.PartitionerAdm

open Argu
open System
open Softellect.DistributedProcessing.PartitionerAdm.CommandLine
open Softellect.DistributedProcessing.PartitionerAdm.Implementation

module Program =

    let partitionerAdmMain programName argv =
        let parser = ArgumentParser.Create<PartitionerAdmArgs>(programName = programName)
        let results = (parser.Parse argv).GetAllResults()

        results
        |> List.iter (fun r ->
                match r with
                | AddSolver solverArgs ->
                    let r = solverArgs.GetAllResults()

                    r
                    |> List.iter (fun x ->
                            match x with
                            | Id id -> printfn $"SolverId: '{id}'."
                            | Name name -> printfn $"Name: '{name}'."
                            | Folder folder -> printfn $"Folder: '{folder}'."
                            | Description description -> printfn $"Description: '{description}'."
                        )

                    addSolver r |> ignore
                | Test testArgs ->
                    let r = testArgs.GetAllResults()

                    r
                    |> List.iter (fun x ->
                            match x with
                            | TestId id -> printfn $"Id: {id}."
                        )
            )
        |> ignore

        0
