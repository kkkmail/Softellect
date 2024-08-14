namespace Softellect.MessagingService

open Argu
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.Messaging.Primitives
open Softellect.MessagingService
open Softellect.MessagingService.CommandLine
open Softellect.Sys.ExitErrorCodes
open Softellect.MessagingService.Worker
open Softellect.Messaging.Service
open Softellect.Wcf.Service

module Program =

    type MessagingProgramData<'D> =
        {
            messagingDataVersion : MessagingDataVersion
            messagingServiceData : MessagingServiceData<'D>
            wcfServiceData :  WcfServiceData<MessagingServiceData<'D>>
        }


    let private createHostBuilder<'D> (data : MessagingProgramData<'D>) =
        Host.CreateDefaultBuilder()
            .UseWindowsService()
            .ConfigureServices(fun hostContext services ->
                services.AddSingleton(data.messagingDataVersion) |> ignore
                services.AddSingleton(data.messagingServiceData) |> ignore
                services.AddSingleton(data.wcfServiceData) |> ignore
                services.AddSingleton<MessagingWcfServiceImpl<'D>>() |> ignore
                services.AddSingleton<MessagingService<'D>>() |> ignore
                services.AddSingleton<IHostedService, MsgWorker<'D>>() |> ignore)


    let main<'D> messagingProgramName data argv =
        let runHost() = createHostBuilder<'D>(data).Build().Run()

        try
            let parser = ArgumentParser.Create<MsgSvcArguArgs>(programName = messagingProgramName)
            let results = (parser.Parse argv).GetAllResults() |> MsgSvcArgs.fromArgu convertArgs

            let run p =
                getParams data.messagingDataVersion p |> ignore
                runHost

            match MessagingServiceTask.tryCreate run (getSaveSettings data.messagingDataVersion) results with
            | Some task -> task.run()
            | None ->  runHost()

            CompletedSuccessfully

        with
        | exn ->
            printfn $"%s{exn.Message}"
            UnknownException
