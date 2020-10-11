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

    let private toError e = e |> SingleErr |> Error
    let private addError e f = (SingleErr e) + f


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
            let fail e : WcfResult<WcfServiceAccessInfo> = e |> WcfCriticalErr |> SingleErr |> Error

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
            err |> SingleErr |> logger.logErrData
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
            | None -> SingleErr WcfServiceCannotInitializeErr |> Error

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


    //let createService g c =
    //    match g() with
    //    | Some data -> c data
    //    | None ->
    //        let errMessage = sprintf "MessagingService<%s, %s>: Data is unavailable." typedefof<'D>.Name typedefof<'E>.Name
    //        printfn "%s" errMessage
    //        failwith errMessage




    //type ServiceBidnerData<'Service, 'Data, 'E> =
    //    {
    //        data : 'Data
    //        creator: 'Data -> ResultWithErr<'Service, 'E>
    //    }


    //[<AbstractClass>]
    //type ServiceBider<'Service, 'Data, 'E> (data : 'Data, creator: 'Data -> ResultWithErr<'Service, 'E>) =
    //    static let mutable getData : unit -> 'Data option = fun () -> None

    //    static let createService() : ResultWithErr<'Service, 'E> =
    //        match getData() with
    //        | Some data -> creator data
    //        | None ->
    //            let errMessage = sprintf "MessagingService<%s, %s>: Data is unavailable." typedefof<'D>.Name typedefof<'E>.Name
    //            printfn "%s" errMessage
    //            failwith errMessage


    //    member _.x = 0
