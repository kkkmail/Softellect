namespace Softellect.MessagingService

open Argu
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.MessagingService
open Softellect.MessagingService.CommandLine
open Softellect.Sys.ExitErrorCodes

module Program =

    let private createHostBuilder<'D>() =
        Host.CreateDefaultBuilder()
            .UseWindowsService()
            .ConfigureServices(fun hostContext services -> services.AddHostedService<MsgWorker<'D>>() |> ignore)


    let main<'D> messagingProgramName argv =
        let runHost() = createHostBuilder<'D>().Build().Run()

        try
            let parser = ArgumentParser.Create<MsgSvcArguArgs>(programName = messagingProgramName)
            let results = (parser.Parse argv).GetAllResults() |> MsgSvcArgs.fromArgu convertArgs

            let run p =
                getParams p |> ignore
                runHost

            match MessagingServiceTask.tryCreate run getSaveSettings results with
            | Some task -> task.run()
            | None ->  runHost()

            CompletedSuccessfully

        with
        | exn ->
            printfn $"%s{exn.Message}"
            UnknownException
