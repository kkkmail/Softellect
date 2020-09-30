namespace Softellect.Communication

open System.Net
open CoreWCF
open CoreWCF.Configuration
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

open Softellect.Core.Primitives
open Softellect.Core.GeneralErrors
open Softellect.Communication.Wcf

module WcfServer =

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


    type WcfService<'S, 'I when 'I : not struct and 'S : not struct>() =
        static let tryCreateWebHostBuilder (i : WcfServiceAccessInfo option) : WcfResult<IWebHost> =
            match i with
            | Some info ->
                try
                    let ipAddress = info.ipAddress
                    let httpPort = info.httpPort
                    let netTcpPort = info.netTcpPort
                    let endPoint : IPEndPoint = new IPEndPoint(ipAddress, httpPort)
                    info.logInfo (sprintf "ipAddress = %A, httpPort = %A, netTcpPort = %A" ipAddress httpPort netTcpPort)

                    let applyOptions (options : KestrelServerOptions) = options.Listen(endPoint)

                    WebHost
                        .CreateDefaultBuilder()
                        .UseKestrel(fun options -> applyOptions options)
                        .UseNetTcp(netTcpPort)
                        .UseStartup<WcfStartup<'S, 'I>>()
                        .Build()
                    |> Ok
                with
                | e -> WcfExn e |> Error
            | None -> WcfServiceNotInitializedErr |> Error

        static let service : Lazy<WcfResult<IWebHost>> =
            new Lazy<WcfResult<IWebHost>>(fun () -> tryCreateWebHostBuilder WcfServiceAccessInfo<'S>.serviceAccessInfo)
            
        static member setInfo i = WcfServiceAccessInfo<'S>.setInfo i
        static member getService() = service.Value

        static member getService i =
             WcfService<'S, 'I>.setInfo i
             WcfService<'S, 'I>.getService()
