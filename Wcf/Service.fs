namespace Softellect.Wcf

open System.Net
open CoreWCF
open CoreWCF.Configuration
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open System.Threading
open Microsoft.FSharp.Core.Operators

open Softellect.Sys.WcfErrors
open Softellect.Sys.Core
open Softellect.Wcf.Common

module Service =

    /// Service reply.
    let tryReply p f a =
        let reply =
            match tryDeserialize wcfSerializationFormat a with
            | Ok m -> p m
            | Error e -> toWcfSerializationError f e

        match trySerialize wcfSerializationFormat reply with
        | Ok r -> r
        | Error _ -> [||]


    type WcfServiceAccessInfo =
        {
            ipAddress : IPAddress
            httpPort : int
            httpServiceName : string
            netTcpPort : int
            netTcpServiceName : string
            logError : (string -> unit)
            logInfo : (string -> unit)
        }

        static member tryCreate (i : ServiceAccessInfo) =
            let logError = i.logError |> Option.defaultValue (printfn "Error: %s")
            let logInfo = i.logInfo |> Option.defaultValue (printfn "Error: %s")

            let fail e =
                logError e
                None

            match IPAddress.TryParse i.serviceAddress.value, i.httpServicePort = i.netTcpServicePort with
            | (true, ipAddress), false ->
                {
                    ipAddress = ipAddress
                    httpPort = i.httpServicePort.value
                    httpServiceName = i.httpServiceName.value
                    netTcpPort = i.netTcpServicePort.value
                    netTcpServiceName = i.netTcpServiceName.value
                    logError = logError
                    logInfo = logInfo
                }
                |> Some

            | (true, _), true ->
                fail (sprintf "http service port: %A must be different from nettcp service port: %A" i.httpServicePort i.netTcpServicePort)
            | (false, _), false ->
                fail (sprintf "invalid IP address: %s" i.serviceAddress.value)
            | (false, _), true ->
                fail (sprintf "invalid IP address: %s and http service port: %A must be different from nettcp service port: %A" i.serviceAddress.value i.httpServicePort i.netTcpServicePort)


    type WcfServiceAccessInfo<'S>() =
        static let mutable info : WcfServiceAccessInfo option = None

        static member setInfo i = info <- WcfServiceAccessInfo.tryCreate i
        static member serviceAccessInfo = info
        member _.serviceType = typeof<'S>


    type WcfStartup<'S, 'I when 'I : not struct and 'S : not struct>() =
        let accessInfo = WcfServiceAccessInfo<'S>.serviceAccessInfo

        let createServiceModel (builder : IServiceBuilder) = 
            match accessInfo with
            | Some i ->
                builder
                    .AddService<'S>()
                    .AddServiceEndpoint<'S, 'I>(new BasicHttpBinding(), "/" + i.httpServiceName)
                    .AddServiceEndpoint<'S, 'I>(new NetTcpBinding(), "/" + i.netTcpServiceName)
                |> ignore
            | None -> failwith "Service access information is missing."

        member _.ConfigureServices(services : IServiceCollection) =
            do services.AddServiceModelServices() |> ignore

        member _.Configure(app : IApplicationBuilder, env : IHostingEnvironment) =
            do app.UseServiceModel(fun builder -> createServiceModel builder) |> ignore


    /// Wrapper around IWebHost to abstract it away and convert C# async methods into F# flavor.
    type WcfService(host : IWebHost, logger : (string -> unit)) =
        let runTokenSource = new CancellationTokenSource()
        let stopTokenSoruce = new CancellationTokenSource()
        let shutDownTokenSource = new CancellationTokenSource()

        let logErr e = 
            let err = e |> WcfExn |> Error
            logger (sprintf "%A" err)
            err

        let tryExecute g = tryExecute (fun () -> g() |> Ok) logErr

        member _.run() = tryExecute host.Run
        member _.runAsync() = async { do! host.RunAsync runTokenSource.Token |> Async.AwaitTask }
        member _.stopAsync() = async { do! host.StopAsync stopTokenSoruce.Token |> Async.AwaitTask }
        member _.waitForShutdown() = host.WaitForShutdown()
        member _.waitForShutdownAsync() = async { do! host.WaitForShutdownAsync shutDownTokenSource.Token |> Async.AwaitTask }

        member _.cancelRunAsync() = tryExecute runTokenSource.Cancel
        member _.cancelStopAsync() = tryExecute stopTokenSoruce.Cancel
        member _.cancelWaitForShutdownAsync() = tryExecute shutDownTokenSource.Cancel


    type WcfService<'S, 'I when 'I : not struct and 'S : not struct>() =
        static let tryCreateWebHostBuilder (i : WcfServiceAccessInfo option) : WcfResult<WcfService> =
            match i with
            | Some info ->
                try
                    info.logInfo (sprintf "ipAddress = %A, httpPort = %A, netTcpPort = %A" info.ipAddress info.httpPort info.netTcpPort)
                    let endPoint : IPEndPoint = new IPEndPoint(info.ipAddress, info.httpPort)
                    let applyOptions (options : KestrelServerOptions) = options.Listen(endPoint)

                    let host =
                        WebHost
                            .CreateDefaultBuilder()
                            .UseKestrel(fun options -> applyOptions options)
                            .UseNetTcp(info.netTcpPort)
                            .UseStartup<WcfStartup<'S, 'I>>()
                            .Build()
                    (host, info.logError) |> WcfService |> Ok
                with
                | e -> WcfExn e |> Error
            | None -> WcfServiceNotInitializedErr |> Error

        static let service : Lazy<WcfResult<WcfService>> =
            new Lazy<WcfResult<WcfService>>(fun () -> tryCreateWebHostBuilder WcfServiceAccessInfo<'S>.serviceAccessInfo)
            
        static member setInfo i = WcfServiceAccessInfo<'S>.setInfo i
        static member getService() = service.Value

        static member getService i =
             WcfService<'S, 'I>.setInfo i
             WcfService<'S, 'I>.getService()
