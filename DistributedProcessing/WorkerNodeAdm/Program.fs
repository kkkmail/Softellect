namespace Softellect.DistributedProcessing.WorkerNodeAdm

open Argu
open Softellect.DistributedProcessing.WorkerNodeAdm.CommandLine
open Softellect.DistributedProcessing.WorkerNodeAdm.Implementation
open Softellect.Sys.ExitErrorCodes

module Program =

    let workerNodeAdmMain programName argv =
        let parser = ArgumentParser.Create<WorkerNodeAdmArgs>(programName = programName)
        let ctx = WorkerNodeAdmContext.create()
        let results = (parser.Parse argv).GetAllResults()

        let retVal =
            results
            |> List.map (fun r ->
                    match r with
                    | GenerateKeys generateKeysArgs -> generateKeys ctx (generateKeysArgs.GetAllResults())
                    | ExportPublicKey exportPublicKeyArgs -> exportPublicKey ctx (exportPublicKeyArgs.GetAllResults())
                    | ImportPublicKey importPublicKeyArgs -> importPublicKey ctx (importPublicKeyArgs.GetAllResults())
                )

        retVal |> List.map (fun x -> printfn $"%A{x}") |> ignore
        CompletedSuccessfully
