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
open Softellect.Sys.Core
open Softellect.Wcf.Common

module Service =

    let private toError e = e |> Error


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
            let h = i.httpServiceInfo
            let n = i.netTcpServiceInfo

            match IPAddress.TryParse h.httpServiceAddress.value, h.httpServicePort = n.netTcpServicePort, h.httpServiceAddress = n.netTcpServiceAddress with
            | (true, ipAddress), false, true ->
                {
                    ipAddress = ipAddress
                    httpPort = h.httpServicePort.value
                    httpServiceName = h.httpServiceName.value
                    netTcpPort = n.netTcpServicePort.value
                    netTcpServiceName = n.netTcpServiceName.value
                }
                |> Ok

            | (true, _), true, _ ->
                fail (sprintf "http service port: %A must be different from nettcp service port: %A" h.httpServicePort n.netTcpServicePort)
            | (false, _), false, _ ->
                fail (sprintf "invalid IP address: %s" h.httpServiceAddress.value)
            | (false, _), true, _ ->
                fail (sprintf "invalid IP address: %s and http service port: %A must be different from nettcp service port: %A" h.httpServiceAddress.value h.httpServicePort n.netTcpServicePort)
            | _, _, false ->
                fail (sprintf "http IP address: %s and net tcp IP address: %s must be the same" h.httpServiceAddress.value n.netTcpServiceAddress.value)

        static member defaultValue =
            {
                ipAddress = IPAddress.None
                httpPort = -1
                httpServiceName = String.Empty
                netTcpPort = - 1
                netTcpServiceName  = String.Empty
            }


    type WcfServiceProxy =
        {
            wcfLogger : WcfLogger
        }


    type WcfServiceData<'P> =
        {
            wcfServiceAccessInfo : WcfServiceAccessInfo
            wcfServiceProxy : WcfServiceProxy
            serviceData : 'P
            setData : 'P -> unit
        }


    type WcfServiceData<'S, 'P>() =
        static let mutable serviceDataOpt :  WcfServiceData<'P> option = None

        static member setData data =
            serviceDataOpt <- Some data
            data.setData data.serviceData

        static member tryGetData() = serviceDataOpt

        /// It is not really needed but we want to "use" the generic type to make the compliler happy.
        member _.serviceType = typeof<'S>


    type WcfStartup<'S, 'I, 'P when 'I : not struct and 'S : not struct>() =
        let data = WcfServiceData<'S, 'P>.tryGetData()

        let createServiceModel (builder : IServiceBuilder) =
            match data with
            | Some d ->
                let i = d.wcfServiceAccessInfo
                builder
                    .AddService<'S>()
                    .AddServiceEndpoint<'S, 'I>(getBasicHttpBinding(), "/" + i.httpServiceName)
                    .AddServiceEndpoint<'S, 'I>(getNetTcpBinding(), "/" + i.netTcpServiceName)
                |> ignore
            | None -> failwith (sprintf "Service data is missing.")

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
            err |> logger.logErrorData
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


    type WcfService<'S, 'I, 'P when 'I : not struct and 'S : not struct>() =
        static let tryCreateWebHostBuilder (data : WcfServiceData<'P> option) : WcfResult<WcfService> =
            match data with
            | Some d ->
                let info = d.wcfServiceAccessInfo
                let logger = d.wcfServiceProxy.wcfLogger
                try
                    logger.logInfoString (sprintf "ipAddress = %A, httpPort = %A, netTcpPort = %A" info.ipAddress info.httpPort info.netTcpPort)
                    let endPoint : IPEndPoint = new IPEndPoint(info.ipAddress, info.httpPort)

                    let applyOptions (options : KestrelServerOptions) =
                        options.Listen(endPoint)
                        options.Limits.MaxResponseBufferSize <- (System.Nullable (int64 Int32.MaxValue))
                        options.Limits.MaxRequestBufferSize <- (System.Nullable (int64 Int32.MaxValue))
                        options.Limits.MaxRequestBodySize <- (System.Nullable (int64 Int32.MaxValue))

                    let host =
                        WebHost
                            .CreateDefaultBuilder()
                            .UseKestrel(fun options -> applyOptions options)
                            .UseNetTcp(info.netTcpPort)
                            .UseStartup<WcfStartup<'S, 'I, 'P>>()
                            .Build()
                    (logger, host) |> WcfService |> Ok
                with
                | e -> WcfExn e |> toError
            | None -> WcfServiceCannotInitializeErr |> Error

        static let service : Lazy<WcfResult<WcfService>> =
            new Lazy<WcfResult<WcfService>>(fun () -> WcfServiceData<'S, 'P>.tryGetData() |> tryCreateWebHostBuilder)
            
        static member setData data = WcfServiceData<'S, 'P>.setData data
        static member tryGetService() = service.Value

        static member tryGetService data =
             WcfService<'S, 'I, 'P>.setData data
             service.Value


    let getData<'Service, 'Data> defaultValue =
        WcfServiceData<'Service, 'Data>.tryGetData()
        |> Option.bind (fun e -> Some e.serviceData)
        |> Option.defaultValue defaultValue
