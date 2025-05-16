namespace Softellect.DistributedProcessing.PartitionerAdm

open Argu
open Softellect.DistributedProcessing.PartitionerAdm.CommandLine
open Softellect.DistributedProcessing.PartitionerAdm.Implementation
open Softellect.Sys.ExitErrorCodes
open Softellect.Sys.AppSettings
open Softellect.Sys.Logging

module Program =

    let partitionerAdmMain programName argv =
        setLogLevel()
        let parser = ArgumentParser.Create<PartitionerAdmArgs>(programName = programName)
        let ctx = PartitionerAdmContext.create()
        let results = (parser.Parse argv).GetAllResults()

        let retVal =
            results
            |> List.map (fun r ->
                    match r with
                    | AddSolver addSolverArgs -> addSolver ctx (addSolverArgs.GetAllResults())
                    | SendSolver sendSolverArgs -> sendSolver ctx (sendSolverArgs.GetAllResults())
                    | SendAllSolvers sendAllSolversArgs -> sendAllSolvers ctx (sendAllSolversArgs.GetAllResults())
                    | AddWorkerNodeService addWorkerNodeArgs -> addWorkerNodeService ctx (addWorkerNodeArgs.GetAllResults())
                    | SendWorkerNodeService sendWorkerNodeArgs -> sendWorkerNodeService ctx (sendWorkerNodeArgs.GetAllResults())
                    | ModifyRunQueue modifyRunQueueArgs -> modifyRunQueue ctx (modifyRunQueueArgs.GetAllResults())
                    | GenerateKeys generateKeysArgs -> generateKeys ctx (generateKeysArgs.GetAllResults())
                    | ExportPublicKey exportPublicKeyArgs -> exportPublicKey ctx (exportPublicKeyArgs.GetAllResults())
                    | ImportPublicKey importPublicKeyArgs -> importPublicKey ctx (importPublicKeyArgs.GetAllResults())
                )

        retVal |> List.map (fun x -> Logger.logInfo $"%A{x}") |> ignore
        CompletedSuccessfully
