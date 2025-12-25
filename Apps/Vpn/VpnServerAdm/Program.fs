namespace Softellect.Vpn.ServerAdm

open Argu
open Softellect.Sys.ExitErrorCodes
open Softellect.Sys.AppSettings
open Softellect.Sys.Logging
open Softellect.Vpn.ServerAdm.CommandLine
open Softellect.Vpn.ServerAdm.Implementation

module Program =

    [<EntryPoint>]
    let main argv =
        setLogLevel()
        let parser = ArgumentParser.Create<VpnServerAdmArgs>(programName = "VpnServerAdm")

        try
            let ctx = ServerAdmContext.create()
            let results = (parser.Parse argv).GetAllResults()

            let retVal =
                results
                |> List.map (fun r ->
                        match r with
                        | GenerateKeys args -> generateKeys ctx (args.GetAllResults())
                        | ExportPublicKey args -> exportPublicKey ctx (args.GetAllResults())
                        | ImportClientKey args -> importClientKey ctx (args.GetAllResults())
                        | RegisterClient args -> registerClient ctx (args.GetAllResults())
                        | ListClients args -> listClients ctx (args.GetAllResults())
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
