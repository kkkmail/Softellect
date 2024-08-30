namespace Softellect.Wcf

open Argu
open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Softellect.Sys.ExitErrorCodes
open Softellect.Wcf.Service
open Softellect.Wcf.Common
open System.Net
open CoreWCF.Configuration
open Microsoft.AspNetCore.Hosting
open Microsoft.FSharp.Core.Operators
open Softellect.Sys.AppSettings

module Program =

    /// IService is the underlying service that does the actual work.
    /// WcfService is the implementation of the WCF service.
    type ProgramData<'IService, 'WcfService> =
        {
            serviceAccessInfo : ServiceAccessInfo
            getService : unit -> 'IService
            getWcfService : 'IService -> 'WcfService
            saveSettings : unit -> unit
            configureServices : (IServiceCollection -> unit) option
        }


    /// IService is the underlying service that does the actual work.
    /// IWcfService is the WCF service interface that is exposed to the client.
    /// WcfService is the implementation of the WCF service.
    let private createHostBuilder<'IService, 'IWcfService, 'WcfService when 'IService : not struct and 'IWcfService : not struct and 'WcfService : not struct>
        (data : ProgramData<'IService, 'WcfService>) =
        Host.CreateDefaultBuilder()
            .UseWindowsService()

            .ConfigureLogging(fun logging ->
                logging.ClearProviders() |> ignore
                logging.AddConsole() |> ignore  // Add console logging
                logging.AddDebug() |> ignore    // Add debug logging
                logging.SetMinimumLevel(LogLevel.Information) |> ignore) // Set minimum log level

            .ConfigureServices(fun hostContext services ->
                let service = data.getService()
                services.AddSingleton<'IService>(service) |> ignore

                let wcfService = data.getWcfService(service)
                services.AddSingleton<'WcfService>(wcfService) |> ignore

                match data.configureServices with
                | Some configure -> configure services |> ignore
                | None -> ()
                )

            .ConfigureWebHostDefaults(fun webBuilder ->
                match data.serviceAccessInfo with
                | HttpServiceInfo i ->
                    webBuilder.UseKestrel(fun options ->
                        let endPoint = IPEndPoint(i.httpServiceAddress.value.ipAddress, i.httpServicePort.value)
                        options.Listen(endPoint)
                        options.Limits.MaxResponseBufferSize <- (System.Nullable (int64 Int32.MaxValue))
                        options.Limits.MaxRequestBufferSize <- (System.Nullable (int64 Int32.MaxValue))
                        options.Limits.MaxRequestBodySize <- (System.Nullable (int64 Int32.MaxValue))) |> ignore
                | NetTcpServiceInfo i ->
                    webBuilder.UseNetTcp(i.netTcpServicePort.value) |> ignore

                webBuilder.UseStartup(fun _ -> WcfStartup<'IWcfService, 'WcfService>(data.serviceAccessInfo)) |> ignore)


    let main<'IService, 'IWcfService, 'WcfService when 'IService : not struct and 'IWcfService : not struct and 'WcfService : not struct> programName data argv =
        printfn $"main<{typeof<'IService>.Name}, {typeof<'IWcfService>.Name}, {typeof<'WcfService>.Name}> - data.serviceAccessInfo = '{data.serviceAccessInfo}'."
        let runHost() = createHostBuilder<'IService, 'IWcfService, 'WcfService>(data).Build().Run()

        try
            let parser = ArgumentParser.Create<SettingsArguments>(programName = programName)
            let results = (parser.Parse argv).GetAllResults()

            match SettingsTask.tryCreate data.saveSettings results with
            | Some task -> task.run()
            | None -> runHost()

            CompletedSuccessfully

        with
        | exn ->
            printfn $"%s{exn.Message}"
            UnknownException
