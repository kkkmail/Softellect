namespace Softellect.Wcf

open Argu
open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Softellect.Sys.ExitErrorCodes
open Softellect.Wcf.Service
open Softellect.Wcf.Common
open Softellect.Sys.Primitives
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
            configureServiceLogging : ILoggingBuilder -> unit
            configureLogging : ILoggingBuilder -> unit
            postBuildHandler : (ServiceAccessInfo -> IHost -> unit) option
        }


    /// IService is the underlying service that does the actual work.
    /// IWcfService is the WCF service interface exposed to the client.
    /// WcfService is the implementation of the WCF service.
    let private createHostBuilder<'IService, 'IWcfService, 'WcfService
        when 'IService :> IHostedService and 'IService : not struct
        and 'IWcfService : not struct
        and 'WcfService : not struct>
        (data : ProgramData<'IService, 'WcfService>) =
        Host.CreateDefaultBuilder()
            .UseWindowsService()

            .ConfigureLogging(fun logging ->
                match isService() with
                | true -> data.configureServiceLogging logging
                | false -> data.configureLogging logging)

            .ConfigureServices(fun hostContext services ->
                let service = data.getService()
                services.AddSingleton<'IService>(service) |> ignore
                services.AddSingleton<IHostedService>(service :> IHostedService) |> ignore

                let wcfService = data.getWcfService(service)
                services.AddSingleton<'WcfService>(wcfService) |> ignore

                match data.configureServices with
                | Some configure -> configure services
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


    let wcfMain<'IService, 'IWcfService, 'WcfService
        when 'IService :> IHostedService and 'IService : not struct
        and 'IWcfService : not struct
        and 'WcfService : not struct> programName data argv =

        let logStarting() =
            Softellect.Sys.Logging.Logger.logInfo $"wcfMain<{typeof<'IService>.Name}, {typeof<'IWcfService>.Name}, {typeof<'WcfService>.Name}> - data.serviceAccessInfo = '{data.serviceAccessInfo}'."

        let runHost() =
            let host = createHostBuilder<'IService, 'IWcfService, 'WcfService>(data).Build()
            logStarting()

            match data.postBuildHandler with
            | Some p -> p data.serviceAccessInfo host
            | None -> ()

            host.Run()

        try
            let parser = ArgumentParser.Create<SettingsArguments>(programName = programName)
            let results = (parser.Parse argv).GetAllResults()

            match SettingsTask.tryCreate data.saveSettings results with
            | Some task ->
                logStarting()
                task.run()
            | None -> runHost()

            CompletedSuccessfully

        with
        | exn ->
            Softellect.Sys.Logging.Logger.logCrit $"%s{exn.Message}"
            UnknownException
