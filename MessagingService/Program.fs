namespace Softellect.MessagingService

open Argu
open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Softellect.Sys.ExitErrorCodes
open Softellect.Messaging.Service
open Softellect.Wcf.Service
open Softellect.Wcf.Common
open System.Net
open CoreWCF.Configuration
open Microsoft.AspNetCore.Hosting
open Microsoft.FSharp.Core.Operators
open Softellect.Messaging.ServiceInfo
open Softellect.Sys.AppSettings
open Softellect.Messaging.AppSettings
open Softellect.Wcf.Program

module Program =

    //let private createHostBuilder<'D> (data : MessagingServiceData<'D>) =
    //    let serviceAccessInfo = data.messagingServiceAccessInfo.serviceAccessInfo
    //    //let x : ILogger = failwith ""
    //    //x.LogCritical("createHostBuilder")
    //    //x.LogDebug("createHostBuilder")

    //    Host.CreateDefaultBuilder()
    //        .UseWindowsService()

    //        .ConfigureLogging(fun logging ->
    //            logging.ClearProviders() |> ignore
    //            logging.AddConsole() |> ignore  // Add console logging
    //            logging.AddDebug() |> ignore    // Add debug logging
    //            logging.SetMinimumLevel(LogLevel.Information) |> ignore) // Set minimum log level

    //        .ConfigureServices(fun hostContext services ->
    //            let messagingService = new MessagingService<'D>(data)
    //            services.AddSingleton<IMessagingService<'D>>(messagingService) |> ignore)

    //        .ConfigureWebHostDefaults(fun webBuilder ->
    //            match serviceAccessInfo with
    //            | HttpServiceInfo i ->
    //                webBuilder.UseKestrel(fun options ->
    //                    let endPoint = IPEndPoint(i.httpServiceAddress.value.ipAddress, i.httpServicePort.value)
    //                    options.Listen(endPoint)
    //                    options.Limits.MaxResponseBufferSize <- (System.Nullable (int64 Int32.MaxValue))
    //                    options.Limits.MaxRequestBufferSize <- (System.Nullable (int64 Int32.MaxValue))
    //                    options.Limits.MaxRequestBodySize <- (System.Nullable (int64 Int32.MaxValue))) |> ignore
    //            | NetTcpServiceInfo i ->
    //                webBuilder.UseNetTcp(i.netTcpServicePort.value) |> ignore

    //            webBuilder.UseStartup(fun _ -> WcfStartup<MessagingWcfService<'D>, IMessagingWcfService>(serviceAccessInfo)) |> ignore)


    //let main<'D> messagingProgramName data argv =
    //    printfn $"main<{typeof<'D>.Name}> - data.messagingServiceAccessInfo = '{data.messagingServiceAccessInfo}'."

    //    let runHost() = createHostBuilder<'D>(data).Build().Run()

    //    try
    //        let parser = ArgumentParser.Create<SettingsArguments>(programName = messagingProgramName)
    //        let results = (parser.Parse argv).GetAllResults()

    //        let saveSettings() =
    //            let result = updateMessagingServiceAccessInfo data.messagingServiceAccessInfo
    //            printfn $"saveSettings - result: '%A{result}'."

    //        match SettingsTask.tryCreate saveSettings results with
    //        | Some task -> task.run()
    //        | None -> runHost()

    //        CompletedSuccessfully

    //    with
    //    | exn ->
    //        printfn $"%s{exn.Message}"
    //        UnknownException


    let main<'D> messagingProgramName data argv =
        printfn $"main<{typeof<'D>.Name}> - data.messagingServiceAccessInfo = '{data.messagingServiceAccessInfo}'."

        let saveSettings() =
            let result = updateMessagingServiceAccessInfo data.messagingServiceAccessInfo
            printfn $"saveSettings - result: '%A{result}'."

        let programData =
            {
                serviceAccessInfo = data.messagingServiceAccessInfo.serviceAccessInfo
                getService = fun () -> new MessagingService<'D>(data) :> IMessagingService<'D>
                getWcfService = fun service -> new MessagingWcfService<'D>(service)
                saveSettings = saveSettings
            }

        main<IMessagingService<'D>, IMessagingWcfService, MessagingWcfService<'D>> messagingProgramName programData argv

        //let runHost() = createHostBuilder<'D>(data).Build().Run()

        //try
        //    let parser = ArgumentParser.Create<SettingsArguments>(programName = messagingProgramName)
        //    let results = (parser.Parse argv).GetAllResults()

        //    let saveSettings() =
        //        let result = updateMessagingServiceAccessInfo data.messagingServiceAccessInfo
        //        printfn $"saveSettings - result: '%A{result}'."

        //    match SettingsTask.tryCreate saveSettings results with
        //    | Some task -> task.run()
        //    | None -> runHost()

        //    CompletedSuccessfully

        //with
        //| exn ->
        //    printfn $"%s{exn.Message}"
        //    UnknownException
