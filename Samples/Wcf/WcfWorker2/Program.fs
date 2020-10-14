//namespace Softellect.Samples.Wcf.WcfWorker
namespace WebTestWorker

open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

module Program =
    //let createHostBuilder args =
    //    Host.CreateDefaultBuilder(args)
    //        .UseWindowsService()
    //        .ConfigureServices(fun hostContext services ->
    //            services.AddHostedService<Worker>() |> ignore)


    //[<EntryPoint>]
    //let main args =
    //    createHostBuilder(args).Build().Run()

    //    0

    let exitCode = 0

    let CreateHostBuilder args =
        Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureWebHostDefaults(fun webBuilder ->
                webBuilder.UseStartup<Startup>() |> ignore
            )

    [<EntryPoint>]
    let main args =
        CreateHostBuilder(args).Build().Run()

        exitCode
