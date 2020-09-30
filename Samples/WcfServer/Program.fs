namespace Softellect.Communication.Samples

open System
open System.IO
open System.Net
open CoreWCF.Configuration
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core

open Softellect.Core.Primitives
open Softellect.Core.GeneralErrors
open Softellect.Communication.Wcf
open Softellect.Communication.WcfServer

open Softellect.Communication.Samples.EchoWcfServiceInfo
open Softellect.Communication.Samples.EchoWcfService

open Startup

module Program =

    //let CreateWebHostBuilder() : IWebHostBuilder =
    //    //let applyOptions (options : KestrelServerOptions) = options.ListenLocalhost(8080)

    //    //WebHost
    //    //    .CreateDefaultBuilder(args)
    //    //    .UseKestrel(fun options -> applyOptions options)
    //    //    .UseUrls("http://localhost:8080")
    //    //    .UseNetTcp(8808)
    //    //    .UseStartup<Startup>()

    //    let applyOptions (options : KestrelServerOptions) =
    //        let address : IPAddress = IPAddress.Parse("192.168.1.89")
    //        let port = 8080
    //        let endPoint : IPEndPoint = new IPEndPoint(address, port)
    //        options.Listen(endPoint)

    //    //WebHost
    //    //    .CreateDefaultBuilder()
    //    //    .UseKestrel(fun options -> applyOptions options)
    //    //    .UseNetTcp(8808)
    //    //    //.UseStartup<Startup<EchoWcfService, IEchoWcfService>>()
    //    //    .UseStartup<Startup<EchoWcfService, string>>()

    //    WebHost
    //        .CreateDefaultBuilder()
    //        .UseKestrel(fun options -> applyOptions options)
    //        .UseNetTcp(8808)
    //        .UseStartup<Startup<EchoWcfService, IEchoWcfService>>()


    //let CreateWebHostBuilder2() : IWebHostBuilder =
    //    let address : IPAddress = IPAddress.Parse("192.168.1.89")
    //    let port = 8080
    //    let netTcpPort = 8808
    //    let endPoint : IPEndPoint = new IPEndPoint(address, port)
    //    printfn "address = %A, port = %A, netTcpPort = %A" address port netTcpPort

    //    let applyOptions (options : KestrelServerOptions) = options.Listen(endPoint)

    //    WebHost
    //        .CreateDefaultBuilder()
    //        .UseKestrel(fun options -> applyOptions options)
    //        .UseNetTcp(netTcpPort)
    //        .UseStartup<Startup<EchoWcfService, IEchoWcfService>>()


    [<EntryPoint>]
    let main _ =
        //let host = (CreateWebHostBuilder2()).Build()
        //host.Run()

        WcfServiceAccessInfo<EchoWcfService>.setInfo echoWcfServiceAccessInfo

        match WcfService<EchoWcfService, IEchoWcfService>.getService() with
        | Ok host -> host.Run()
        | Error e -> 
            printfn "Error: %A" e
            Console.ReadLine() |> ignore
        
        0
