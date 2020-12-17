namespace Softellect.Samples.Msg.WcfWorker

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

open System

module Program =

    let createHostBuilder args =
        Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureServices(fun hostContext services ->
                services.AddHostedService<Worker>() |> ignore)


    [<EntryPoint>]
    let main args =
        createHostBuilder(args).Build().Run()

        0
