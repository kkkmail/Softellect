namespace Softellect.Samples.Wcf.NetCoreService

open CoreWCF.Configuration
//open Microsoft.AspNetCore
//open Microsoft.AspNetCore.Hosting
//open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.Hosting
open Microsoft.AspNetCore.Hosting

open Softellect.Samples.Wcf.NetCoreService.Startup

/// Port of C# CoreWCF service example.
module Program =

    let createHostBuilder args =
        failwith "createHostBuilder is not implemented yet."
        // let applyOptions (options : KestrelServerOptions) = options.ListenLocalhost(8080)

        Host.CreateDefaultBuilder()
            //.CreateDefaultBuilder(args)
            // .UseKestrel(fun options -> applyOptions options)
            // .UseUrls("http://localhost:8080")
            // .UseNetTcp(8808)
            // .UseStartup<Startup>()


    [<EntryPoint>]
    let main argv =
        let host = (createHostBuilder argv).Build()
        host.Run()
        0
