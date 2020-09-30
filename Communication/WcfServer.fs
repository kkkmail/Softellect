namespace Softellect.Communication

open System.Net
open CoreWCF
open CoreWCF.Configuration
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

open Softellect.Core.Primitives
open Softellect.Core.GeneralErrors
open Softellect.Communication.Wcf

module WcfServer =

    type WcfServiceAccessInfo =
        {
            serviceAddress : ServiceAddress
            httpServicePort : ServicePort
            netTcpServicePort : ServicePort
            serviceName : ServiceName
        }


    type WcfServiceAccessInfo<'S>() =
        static let mutable info : WcfServiceAccessInfo option = None

        static member setInfo i = info <- Some i
        static member serviceAccessInfo = info
        member _.serviceType = typeof<'S>


    type WcfStartup<'S, 'I when 'I : not struct and 'S : not struct>() =
        let accessInfo = WcfServiceAccessInfo<'S>.serviceAccessInfo

        let createServiceModel (builder : IServiceBuilder) = 
            builder
                .AddService<'S>()
                .AddServiceEndpoint<'S, 'I>(new BasicHttpBinding(), "/basichttp")
                .AddServiceEndpoint<'S, 'I>(new NetTcpBinding(), "/" + accessInfo.Value.serviceName.value)
            |> ignore

        member _.ConfigureServices(services : IServiceCollection) =
            do services.AddServiceModelServices() |> ignore

        member _.Configure(app : IApplicationBuilder, env : IHostingEnvironment) =
            do app.UseServiceModel(fun builder -> createServiceModel builder) |> ignore


    type WcfService<'S, 'I when 'I : not struct and 'S : not struct>() =
        static let createWebHostBuilder (i : WcfServiceAccessInfo option) : WcfResult<IWebHost> =
            match i with
            | Some info ->
                let address : IPAddress = IPAddress.Parse(info.serviceAddress.value)
                let port = info.httpServicePort.value
                let netTcpPort = info.netTcpServicePort.value
                let endPoint : IPEndPoint = new IPEndPoint(address, port)
                printfn "address = %A, port = %A, netTcpPort = %A" address port netTcpPort

                let applyOptions (options : KestrelServerOptions) = options.Listen(endPoint)

                WebHost
                    .CreateDefaultBuilder()
                    .UseKestrel(fun options -> applyOptions options)
                    .UseNetTcp(netTcpPort)
                    .UseStartup<WcfStartup<'S, 'I>>()
                    .Build()
                |> Ok
            | None -> WcfServiceNotInitializedErr |> Error

        static let service : Lazy<WcfResult<IWebHost>> =
            new Lazy<WcfResult<IWebHost>>(fun () -> createWebHostBuilder WcfServiceAccessInfo<'S>.serviceAccessInfo)
            
        static member getService() = service.Value
