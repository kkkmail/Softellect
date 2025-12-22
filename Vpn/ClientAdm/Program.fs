namespace Softellect.Vpn.ClientAdm

open Argu
open Softellect.Sys.ExitErrorCodes
open Softellect.Sys.AppSettings
open Softellect.Sys.Logging
open Softellect.Vpn.ClientAdm.CommandLine
open Softellect.Vpn.ClientAdm.Implementation

module Program =

    [<EntryPoint>]
    let main argv =
        setLogLevel()
        let parser = ArgumentParser.Create<VpnClientAdmArgs>(programName = "VpnClientAdm")

        try
            let ctx = ClientAdmContext.create()
            let results = (parser.Parse argv).GetAllResults()

            let retVal =
                results
                |> List.map (fun r ->
                        match r with
                        | GenerateKeys args -> generateKeys ctx (args.GetAllResults())
                        | ExportPublicKey args -> exportPublicKey ctx (args.GetAllResults())
                        | ImportServerKey args -> importServerKey ctx (args.GetAllResults())
                        | Status args -> status ctx (args.GetAllResults())
                        | SetServer args -> setServer ctx (args.GetAllResults())
                        | DetectPhysicalNetwork -> detectPhysicalNetwork ctx
                    )

            retVal |> List.iter (fun x ->
                match x with
                | Ok msg -> Logger.logInfo msg
                | Error msg -> Logger.logError msg)

            CompletedSuccessfully
        with
        | :? ArguParseException as ex ->
            printfn "%s" ex.Message
            InvalidCommandLineArgs
        | ex ->
            Logger.logCrit $"Unexpected error: {ex.Message}"
            UnknownException
