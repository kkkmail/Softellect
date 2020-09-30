namespace Softellect.Communication.Samples

open CoreWCF
open CoreWCF.Configuration
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection

open Softellect.Communication.Samples.EchoWcfServiceInfo

open EchoWcfService

module Startup =

    //type Startup() =

    //    let createServiceModel (builder : IServiceBuilder) = 
    //        builder
    //            .AddService<EchoWcfService>()
    //            .AddServiceEndpoint<EchoWcfService, IEchoWcfService>(new BasicHttpBinding(), "/basichttp")
    //            .AddServiceEndpoint<EchoWcfService, IEchoWcfService>(new NetTcpBinding(), "/nettcp")
    //        |> ignore

    //    member _.ConfigureServices(services : IServiceCollection) =
    //        do services.AddServiceModelServices() |> ignore

    //    member _.Configure(app : IApplicationBuilder, env : IHostingEnvironment) =
    //        do app.UseServiceModel(fun builder -> createServiceModel builder) |> ignore


    type Startup<'S, 'I when 'I : not struct and 'S : not struct>() =

        let createServiceModel (builder : IServiceBuilder) = 
            builder
                .AddService<'S>()
                .AddServiceEndpoint<'S, 'I>(new BasicHttpBinding(), "/basichttp")
                .AddServiceEndpoint<'S, 'I>(new NetTcpBinding(), "/nettcp")
            |> ignore

        member _.ConfigureServices(services : IServiceCollection) =
            do services.AddServiceModelServices() |> ignore

        member _.Configure(app : IApplicationBuilder, env : IHostingEnvironment) =
            do app.UseServiceModel(fun builder -> createServiceModel builder) |> ignore


    let x = 1

