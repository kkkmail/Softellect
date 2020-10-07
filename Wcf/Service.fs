namespace Softellect.Wcf

open System
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
open Softellect.Sys.Errors
open Softellect.Sys.Core
open Softellect.Wcf.Common
open Softellect.Sys.Logging

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


    /// Gets net tcp binding suitable for sending very large data objects.
    let getNetTcpBinding() =
        let binding = new NetTcpBinding()
        binding.MaxReceivedMessageSize <- (int64 Int32.MaxValue)
        binding.MaxBufferPoolSize <- (int64 Int32.MaxValue)
        binding.MaxBufferSize <- Int32.MaxValue
        binding.OpenTimeout <- connectionTimeOut
        binding.CloseTimeout <- connectionTimeOut
        binding.SendTimeout <- dataTimeOut
        binding.ReceiveTimeout <- dataTimeOut
        binding.Security.Mode <- SecurityMode.Transport
        binding.ReaderQuotas <- getQuotas()
        binding


    /// Gets basic http binding suitable for sending very large data objects.
    let getBasicHttpBinding() =
        let binding = new BasicHttpBinding()
        binding.MaxReceivedMessageSize <- (int64 Int32.MaxValue)
        //binding.MaxBufferPoolSize <- (int64 Int32.MaxValue)
        binding.MaxBufferSize <- Int32.MaxValue
        binding.OpenTimeout <- connectionTimeOut
        binding.CloseTimeout <- connectionTimeOut
        binding.SendTimeout <- dataTimeOut
        binding.ReceiveTimeout <- dataTimeOut
        //binding.Security.Mode <- BasicHttpSecurityMode.None
        binding.ReaderQuotas <- getQuotas()
        binding


    type WcfServiceAccessInfo =
        {
            ipAddress : IPAddress
            httpPort : int
            httpServiceName : string
            netTcpPort : int
            netTcpServiceName : string
        }

        static member tryCreate (i : ServiceAccessInfo) =
            let fail e : WcfResult<WcfServiceAccessInfo> = e |> WcfCriticalErr |> Error

            match IPAddress.TryParse i.serviceAddress.value, i.httpServicePort = i.netTcpServicePort with
            | (true, ipAddress), false ->
                {
                    ipAddress = ipAddress
                    httpPort = i.httpServicePort.value
                    httpServiceName = i.httpServiceName.value
                    netTcpPort = i.netTcpServicePort.value
                    netTcpServiceName = i.netTcpServiceName.value
                }
                |> Ok

            | (true, _), true ->
                fail (sprintf "http service port: %A must be different from nettcp service port: %A" i.httpServicePort i.netTcpServicePort)
            | (false, _), false ->
                fail (sprintf "invalid IP address: %s" i.serviceAddress.value)
            | (false, _), true ->
                fail (sprintf "invalid IP address: %s and http service port: %A must be different from nettcp service port: %A" i.serviceAddress.value i.httpServicePort i.netTcpServicePort)


    type WcfServiceProxy =
        {
            wcfServiceAccessInfoRes : WcfResult<WcfServiceAccessInfo>
            loggerOpt : WcfLogger option
        }

        static member defaultValue =
            {
                wcfServiceAccessInfoRes = WcfServiceNotInitializedErr |> Error
                loggerOpt = None
            }


    type WcfServiceProxy<'S>() =
        static let mutable serviceProxy = WcfServiceProxy.defaultValue
        static member setProxy proxy = serviceProxy <- proxy
        static member proxy = serviceProxy

        /// It is not really needed but we want to "use" the generic type to make the compliler happy.
        member _.serviceType = typeof<'S>


    type WcfStartup<'S, 'I when 'I : not struct and 'S : not struct>() =
        let a = WcfServiceProxy<'S>.proxy.wcfServiceAccessInfoRes

        let createServiceModel (builder : IServiceBuilder) =
            match a with
            | Ok i ->
                builder
                    .AddService<'S>()
                    .AddServiceEndpoint<'S, 'I>(getBasicHttpBinding(), "/" + i.httpServiceName)
                    .AddServiceEndpoint<'S, 'I>(getNetTcpBinding(), "/" + i.netTcpServiceName)
                |> ignore
            | Error e ->
                // TODO kk:20201006 - Log error and then don't throw???
                failwith (sprintf "Service access information is missing: %A." e)

        /// The name must match required signature in CoreWCF.
        member _.ConfigureServices(services : IServiceCollection) =
            do services.AddServiceModelServices() |> ignore

        /// The name must match required signature in CoreWCF.
        member _.Configure(app : IApplicationBuilder, env : IHostingEnvironment) =
            do app.UseServiceModel(fun builder -> createServiceModel builder) |> ignore


    /// Wrapper around IWebHost to abstract it away and convert C# async methods into F# flavor.
    type WcfService(logger : WcfLogger, host : IWebHost) =
        let runTokenSource = new CancellationTokenSource()
        let stopTokenSoruce = new CancellationTokenSource()
        let shutDownTokenSource = new CancellationTokenSource()

        let logErr e =
            let err = e |> WcfExn
            err |> WcfErr |> logger.logErrData
            Error err

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
        static let tryCreateWebHostBuilder (proxy :  WcfServiceProxy) : WcfResult<WcfService> =
            let logger = proxy.loggerOpt |> Option.defaultValue WcfLogger.defaultValue

            match proxy.wcfServiceAccessInfoRes with
            | Ok info ->
                try
                    logger.logInfoString (sprintf "ipAddress = %A, httpPort = %A, netTcpPort = %A" info.ipAddress info.httpPort info.netTcpPort)
                    let endPoint : IPEndPoint = new IPEndPoint(info.ipAddress, info.httpPort)

                    let applyOptions (options : KestrelServerOptions) =
                        options.Listen(endPoint)
                        options.Limits.MaxResponseBufferSize <- (System.Nullable (int64 Int32.MaxValue))
                        options.Limits.MaxRequestBufferSize <- (System.Nullable (int64 Int32.MaxValue))
                        //options.Limits.MaxRequestLineSize <- Int32.MaxValue
                        //options.Limits.MaxRequestHeadersTotalSize <- Int32.MaxValue
                        options.Limits.MaxRequestBodySize <- (System.Nullable (int64 Int32.MaxValue))
                        //options.Limits.MaxRequestHeaderCount <- 500

                    let host =
                        WebHost
                            .CreateDefaultBuilder()
                            .UseKestrel(fun options -> applyOptions options)
                            //.UseNetTcp(info.netTcpPort)
                            .UseStartup<WcfStartup<'S, 'I>>()
                            .Build()
                    (logger, host) |> WcfService |> Ok
                with
                | e -> WcfExn e |> Error
            | Error e -> e |> WcfServiceCannotInitializeErr |> Error

        static let service : Lazy<WcfResult<WcfService>> =
            new Lazy<WcfResult<WcfService>>(fun () -> tryCreateWebHostBuilder WcfServiceProxy<'S>.proxy)
            
        static member setProxy proxy = WcfServiceProxy<'S>.setProxy proxy
        static member getService() = service.Value

        static member getService proxy =
             WcfService<'S, 'I>.setProxy proxy
             WcfService<'S, 'I>.getService()
