namespace Softellect.Communication.Samples

open System.Net
open CoreWCF.Configuration
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core

open Startup

module Program =

    let CreateWebHostBuilder() : IWebHostBuilder =
        //let applyOptions (options : KestrelServerOptions) = options.ListenLocalhost(8080)

        //WebHost
        //    .CreateDefaultBuilder(args)
        //    .UseKestrel(fun options -> applyOptions options)
        //    .UseUrls("http://localhost:8080")
        //    .UseNetTcp(8808)
        //    .UseStartup<Startup>()

        let applyOptions (options : KestrelServerOptions) =
            let address : IPAddress = IPAddress.Parse("192.168.1.89")
            let port = 8080
            let endPoint : IPEndPoint = new IPEndPoint(address, port)
            options.Listen(endPoint)

        WebHost
            .CreateDefaultBuilder()
            .UseKestrel(fun options -> applyOptions options)
            .UseNetTcp(8808)
            .UseStartup<Startup>()


    [<EntryPoint>]
    let main _ =
        let host = (CreateWebHostBuilder()).Build()
        host.Run()
        0
