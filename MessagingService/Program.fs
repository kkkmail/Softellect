namespace Softellect.MessagingService

open Argu
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.Messaging.Primitives
open Softellect.MessagingService
open Softellect.MessagingService.CommandLine
open Softellect.Sys.ExitErrorCodes

module Program =

    let private createHostBuilder<'D> (v : MessagingDataVersion) =
        Host.CreateDefaultBuilder()
            .UseWindowsService()
            .ConfigureServices(fun hostContext services ->
                services.AddSingleton(v) |> ignore
                services.AddHostedService<MsgWorker<'D>>() |> ignore)


    let main<'D> messagingProgramName v argv =
        let runHost() = createHostBuilder<'D>(v).Build().Run()

        try
            let parser = ArgumentParser.Create<MsgSvcArguArgs>(programName = messagingProgramName)
            let results = (parser.Parse argv).GetAllResults() |> MsgSvcArgs.fromArgu convertArgs

            let run p =
                getParams v p |> ignore
                runHost

            match MessagingServiceTask.tryCreate run (getSaveSettings v) results with
            | Some task -> task.run()
            | None ->  runHost()

            CompletedSuccessfully

        with
        | exn ->
            printfn $"%s{exn.Message}"
            UnknownException
