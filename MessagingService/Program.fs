namespace Softellect.MessagingService

open Argu
open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Softellect.Messaging.Primitives
open Softellect.MessagingService
open Softellect.MessagingService.CommandLine
open Softellect.Sys.ExitErrorCodes
open Softellect.MessagingService.Worker
open Softellect.Messaging.Service
open Softellect.Wcf.Service
open System
open System.Net
open CoreWCF
open CoreWCF.Configuration
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open System.Threading
open Microsoft.FSharp.Core.Operators
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Softellect.MessagingService.CommandLine
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Messaging.Primitives


module Program =

    type MessagingProgramData<'D> =
        {
            messagingDataVersion : MessagingDataVersion
            messagingServiceData : MessagingServiceData<'D>
            wcfServiceData :  WcfServiceData<MessagingServiceData<'D>>
        }


    //let private createHostBuilder<'D> (data : MessagingProgramData<'D>) =
    //    printfn $"createHostBuilder<{typeof<'D>.Name}> - data.messagingDataVersion = '{data.messagingDataVersion}'."
    //    printfn $"createHostBuilder<{typeof<'D>.Name}> - data.messagingServiceData = '{data.messagingServiceData}'."
    //    printfn $"createHostBuilder<{typeof<'D>.Name}> - data.wcfServiceData = '{data.wcfServiceData}'."

    //    Host.CreateDefaultBuilder()
    //        .UseWindowsService()
    //        .ConfigureServices(fun hostContext services ->
    //            services.AddSingleton(data.messagingDataVersion) |> ignore
    //            services.AddSingleton(data.messagingServiceData) |> ignore
    //            services.AddSingleton(data.wcfServiceData) |> ignore
    //            services.AddSingleton<MessagingService<'D>>() |> ignore
    //            services.AddSingleton<MessagingWcfServiceImpl<'D>>() |> ignore
    //            services.AddSingleton<IHostedService, MsgWorker<'D>>() |> ignore)

    //let getMsgWorker<'D> (serviceProvider : IServiceProvider) =
    //    let logger = serviceProvider.GetRequiredService<ILogger<MsgWorker<'D>>>()
    //    let messagingDataVersion = serviceProvider.GetRequiredService<MessagingDataVersion>()
    //    let messagingService = serviceProvider.GetRequiredService<MessagingService<'D>>()
    //    let wcfServiceImpl = serviceProvider.GetRequiredService<MessagingWcfServiceImpl<'D>>()
    //    let worker = new MsgWorker<'D>(logger, messagingDataVersion, messagingService, wcfServiceImpl)
    //    worker



    //let private createHostBuilder<'D> (data : MessagingProgramData<'D>) =
    //    Host.CreateDefaultBuilder()
    //        .UseWindowsService()
    //        //.ConfigureLogging(fun logging ->
    //        //    logging.ClearProviders() |> ignore
    //        //    logging.AddConsole() |> ignore
    //        //    logging.AddDebug() |> ignore
    //        //    logging.SetMinimumLevel(LogLevel.Trace) |> ignore)
    //        .ConfigureServices(fun hostContext services ->
    //            // Register individual components
    //            services.AddSingleton(data.messagingDataVersion) |> ignore
    //            services.AddSingleton(data.messagingServiceData) |> ignore
    //            services.AddSingleton(data.wcfServiceData) |> ignore

    //            // Manually create the MessagingService<'D> and register it as a singleton.
    //            let messagingService = MessagingService<'D>(data.messagingServiceData)
    //            services.AddSingleton(messagingService) |> ignore

    //            // Manually create the MessagingWcfService<'D> and register it as a singleton.
    //            let messagingWcfService = MessagingWcfService<'D>(messagingService)
    //            services.AddSingleton(messagingWcfService) |> ignore

    //            // Manually create the MessagingWcfServiceImpl<'D> and register it as a singleton.
    //            let wcfServiceImpl = MessagingWcfServiceImpl<'D>(data.wcfServiceData)
    //            services.AddSingleton(wcfServiceImpl) |> ignore

    //            // Register MsgWorker<'D> as IHostedService with all necessary dependencies.
    //            services.AddSingleton<IHostedService>(fun serviceProvider ->
    //                let logger = serviceProvider.GetRequiredService<ILogger<MsgWorker<'D>>>()
    //                new MsgWorker<'D>(logger, data.messagingDataVersion, messagingService, wcfServiceImpl) :> IHostedService) |> ignore)


    //let private createHostBuilder<'D> (data : MessagingProgramData<'D>) =
    //    Host.CreateDefaultBuilder()
    //        .UseWindowsService()
    //        .ConfigureServices(fun hostContext services ->
    //            services.AddSingleton(data.messagingDataVersion) |> ignore
    //            services.AddSingleton(data.messagingServiceData) |> ignore
    //            services.AddSingleton(data.wcfServiceData) |> ignore

    //            services.AddSingleton<MessagingService<'D>>() |> ignore

    //            services.AddSingleton<IHostedService>(fun serviceProvider ->
    //                let logger = serviceProvider.GetRequiredService<ILogger<MsgWorker<'D>>>()
    //                let messagingService = serviceProvider.GetRequiredService<MessagingService<'D>>()

    //                let wcfServiceImpl = MessagingWcfServiceImpl<'D>(messagingService)
    //                new MsgWorker<'D>(logger, data.messagingDataVersion, messagingService, wcfServiceImpl) :> IHostedService) |> ignore
    //                )

    //        .ConfigureWebHostDefaults(fun webBuilder ->
    //            webBuilder.UseKestrel(fun options ->
    //                let endPoint = IPEndPoint(data.wcfServiceData.wcfServiceAccessInfo.ipAddress, data.wcfServiceData.wcfServiceAccessInfo.httpPort)
    //                options.Listen(endPoint)
    //                options.Limits.MaxResponseBufferSize <- (System.Nullable (int64 Int32.MaxValue))
    //                options.Limits.MaxRequestBufferSize <- (System.Nullable (int64 Int32.MaxValue))
    //                options.Limits.MaxRequestBodySize <- (System.Nullable (int64 Int32.MaxValue))) |> ignore

    //            webBuilder.UseNetTcp(data.wcfServiceData.wcfServiceAccessInfo.netTcpPort) |> ignore
    //            webBuilder.UseStartup(fun _ -> WcfStartup<'Service, 'IWcfService, MessagingServiceData<'D>>(data.wcfServiceData)) |> ignore)


    let private createHostBuilder<'D> (data : MessagingProgramData<'D>) =
        Host.CreateDefaultBuilder()
            .UseWindowsService()
            .ConfigureServices(fun hostContext services ->
                services.AddSingleton(data.messagingDataVersion) |> ignore
                services.AddSingleton(data.messagingServiceData) |> ignore
                services.AddSingleton(data.wcfServiceData) |> ignore

                // Register MessagingService<'D> as a singleton.
                services.AddSingleton<MessagingService<'D>>() |> ignore)

            .ConfigureWebHostDefaults(fun webBuilder ->
                webBuilder.UseKestrel(fun options ->
                    let endPoint = IPEndPoint(data.wcfServiceData.wcfServiceAccessInfo.ipAddress, data.wcfServiceData.wcfServiceAccessInfo.httpPort)
                    options.Listen(endPoint)
                    options.Limits.MaxResponseBufferSize <- (System.Nullable (int64 Int32.MaxValue))
                    options.Limits.MaxRequestBufferSize <- (System.Nullable (int64 Int32.MaxValue))
                    options.Limits.MaxRequestBodySize <- (System.Nullable (int64 Int32.MaxValue))) |> ignore

                webBuilder.UseNetTcp(data.wcfServiceData.wcfServiceAccessInfo.netTcpPort) |> ignore
                webBuilder.UseStartup(fun _ -> WcfStartup<MessagingWcfService<'D>, IMessagingWcfService, MessagingServiceData<'D>>(data.wcfServiceData)) |> ignore)


    let main<'D> messagingProgramName data argv =
        printfn $"main<{typeof<'D>.Name}> - data.messagingDataVersion = '{data.messagingDataVersion}'."
        printfn $"main<{typeof<'D>.Name}> - data.messagingServiceData = '{data.messagingServiceData}'."
        printfn $"main<{typeof<'D>.Name}> - data.wcfServiceData = '{data.wcfServiceData}'."

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
