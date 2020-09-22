namespace Softellect.Communication.Samples

open System
open CoreWCF.Configuration
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core

open Startup

module Program =

    let CreateWebHostBuilder args : IWebHostBuilder =
        let applyOptions (options : KestrelServerOptions) = options.ListenLocalhost(8080)

        WebHost
            .CreateDefaultBuilder(args)
            .UseKestrel(fun options -> applyOptions options)
            .UseUrls("http://localhost:8080")
            .UseNetTcp(8808)
            .UseStartup<Startup>()


    [<EntryPoint>]
    let main argv =
        let host = (CreateWebHostBuilder argv).Build()
        host.Run()
        0
