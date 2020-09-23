namespace Softellect.Communication.Samples

open CoreWCF
open CoreWCF.Configuration
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection

open Softellect.Communication.Samples.EchoWcfServiceInfo

open EchoWcfService

module Startup =

    type IWebHostStartup =
        abstract ConfigureServices : IServiceCollection -> unit
        abstract Configure : IApplicationBuilder * IHostingEnvironment -> unit


    type Startup() =

        let createServiceModel (builder : IServiceBuilder) = 
            builder
                .AddService<EchoWcfService>()
                .AddServiceEndpoint<EchoWcfService, IEchoWcfService>(new BasicHttpBinding(), "/basichttp")
                //.AddServiceEndpoint<EchoWcfService, IEchoWcfService>(new NetTcpBinding(), "/nettcp")
            |> ignore

        //interface IWebHostStartup with
        member _.ConfigureServices(services : IServiceCollection) =
            do services.AddServiceModelServices() |> ignore

        member _.Configure(app : IApplicationBuilder, env : IHostingEnvironment) =
            do app.UseServiceModel(fun builder -> createServiceModel builder) |> ignore

