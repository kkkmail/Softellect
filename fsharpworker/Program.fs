namespace fsharpworker

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
//open Topshelf

/// https://anthonygiretti.com/2020/01/03/building-a-windows-service-with-service-workers-and-net-core-3-1-part-2-migrate-a-timed-service-built-with-topshelf/
module Program =

    let createHostBuilder args =
        //let a =
        //    HostFactory.Run(fun x ->
        //            x.RunAsLocalSystem() |> ignore
        //            x.SetDescription("")
        //            x.SetDisplayName("")
        //            x.SetServiceName("")
        //            ignore())

        Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureServices(fun hostContext services ->
                services.AddHostedService<Worker>() |> ignore)


    [<EntryPoint>]
    let main args =
        createHostBuilder(args).Build().Run()

        0
