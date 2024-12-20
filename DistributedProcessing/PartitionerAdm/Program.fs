namespace Softellect.DistributedProcessing.PartitionerAdm

open Argu
open Softellect.DistributedProcessing.PartitionerAdm.CommandLine
open Softellect.DistributedProcessing.PartitionerAdm.Implementation
open Softellect.Sys.ExitErrorCodes

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
                    | ModifyRunQueue modifyRunQueueArgs -> modifyRunQueue ctx (modifyRunQueueArgs.GetAllResults())
                )

        retVal |> List.map (fun x -> printfn $"%A{x}") |> ignore
        CompletedSuccessfully
