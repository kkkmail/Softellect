namespace Softellect.Samples.Wcf.NetCoreService

open CoreWCF
open CoreWCF.Configuration
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection

open Softellect.Samples.Wcf.NetCoreService.EchoService

module Startup =

    type Startup() =

        let createServiceModel (builder : IServiceBuilder) =
            builder
                .AddService<EchoService>()
                .AddServiceEndpoint<EchoService, IEchoService>(BasicHttpBinding(), "/basichttp")
                .AddServiceEndpoint<EchoService, IEchoService>(NetTcpBinding(), "/nettcp")
            |> ignore

        member _.ConfigureServices(services : IServiceCollection) =
            services.AddServiceModelServices() |> ignore

        member _.Configure(app : IApplicationBuilder, _ : IWebHostEnvironment) =
            app.UseServiceModel(fun builder -> createServiceModel builder) |> ignore
