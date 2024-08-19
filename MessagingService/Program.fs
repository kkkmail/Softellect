namespace Softellect.MessagingService

open Argu
open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Softellect.Messaging.Primitives
open Softellect.Messaging
open Softellect.Messaging.CommandLine
open Softellect.Sys.ExitErrorCodes
open Softellect.Messaging.Service
open Softellect.Wcf.Service
open Softellect.Wcf.Common
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
open Softellect.Messaging.CommandLine
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Messaging.Primitives


module Program =

    type MessagingProgramData<'D> =
        {
            messagingDataVersion : MessagingDataVersion
            wcfServiceData :  WcfServiceData<MessagingServiceData<'D>>
        }


    let private createHostBuilder<'D> (data : MessagingProgramData<'D>) =
        Host.CreateDefaultBuilder()
            .UseWindowsService()
            .ConfigureServices(fun hostContext services ->
                let messagingService = new MessagingService<'D>(data.wcfServiceData.serviceData)
                services.AddSingleton<IMessagingService<'D>>(messagingService) |> ignore)

            .ConfigureWebHostDefaults(fun webBuilder ->
                match data.wcfServiceData.wcfServiceAccessInfo with
                | HttpServiceInfo i ->
                    webBuilder.UseKestrel(fun options ->
                        let endPoint = IPEndPoint(i.httpServiceAddress.value.ipAddress, i.httpServicePort.value)
                        options.Listen(endPoint)
                        options.Limits.MaxResponseBufferSize <- (System.Nullable (int64 Int32.MaxValue))
                        options.Limits.MaxRequestBufferSize <- (System.Nullable (int64 Int32.MaxValue))
                        options.Limits.MaxRequestBodySize <- (System.Nullable (int64 Int32.MaxValue))) |> ignore
                | NetTcpServiceInfo i ->
                    webBuilder.UseNetTcp(i.netTcpServicePort.value) |> ignore

                webBuilder.UseStartup(fun _ -> WcfStartup<MessagingWcfService<'D>, IMessagingWcfService, MessagingServiceData<'D>>(data.wcfServiceData)) |> ignore)


    let main<'D> messagingProgramName data argv =
        printfn $"main<{typeof<'D>.Name}> - data.messagingDataVersion = '{data.messagingDataVersion}'."
        printfn $"main<{typeof<'D>.Name}> - data.messagingServiceData = '{data.wcfServiceData.serviceData}'."
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
