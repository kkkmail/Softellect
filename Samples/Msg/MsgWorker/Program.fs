namespace Softellect.Samples.Msg.WcfWorker

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.MessagingService
open Softellect.MessagingService.Program
open Softellect.Samples.Msg.ServiceInfo.Primitives

module Program =

    //let createHostBuilder args =
    //    Host.CreateDefaultBuilder(args)
    //        .UseWindowsService()
    //        .ConfigureServices(fun hostContext services ->
    //            //services.AddHostedService<Worker>()
    //            services.AddHostedService<MsgWorker<EchoMessageData>>()
    //            |> ignore)


    //[<EntryPoint>]
    //let main args =
    //    createHostBuilder(args).Build().Run()

    //    0

    [<EntryPoint>]
    let main args = main<EchoMessageData> "MsgWorker" args
