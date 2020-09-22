namespace Softellect.Communication.Samples

open CoreWCF
open  CoreWCF.Configuration
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open EchoService

module Startup =

    type Startup() =

        let createServiceModel (builder : IServiceBuilder) = 
            builder
                .AddService<EchoService>()
                .AddServiceEndpoint<EchoService, IEchoService>(new BasicHttpBinding(), "/basichttp")
                .AddServiceEndpoint<EchoService, IEchoService>(new NetTcpBinding(), "/nettcp")
            |> ignore


        member _.ConfigureServices(services : IServiceCollection) =
            do services.AddServiceModelServices() |> ignore

        member _.Configure(app : IApplicationBuilder, env : IHostingEnvironment) =
            do app.UseServiceModel(fun builder -> createServiceModel builder) |> ignore

