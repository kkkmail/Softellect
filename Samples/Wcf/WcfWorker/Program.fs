namespace Softellect.Samples.Wcf.WcfWorker

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

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
