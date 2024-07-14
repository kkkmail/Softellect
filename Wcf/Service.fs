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

open Softellect.Wcf.Errors
open Softellect.Sys.Core
open Softellect.Wcf.Common
open System.Threading.Tasks

module Service =

    let private toError e = e |> Error


    type WcfSecurityMode
        with
        member s.securityMode =
            match s with
            | NoSecurity -> SecurityMode.None
            | TransportSecurity -> SecurityMode.Transport
            | MessageSecurity -> SecurityMode.Message
            | TransportWithMessageCredentialSecurity -> SecurityMode.TransportWithMessageCredential


//    let mutable private serviceCount = 0L


    /// Service reply.
    let tryReply p f a =
//        let count = Interlocked.Increment(&serviceCount)
//        printfn $"tryReply - {count}: Starting..."

        let reply =
            match tryDeserialize wcfSerializationFormat a with
            | Ok m -> p m
            | Error e ->
//                printfn $"tryReply - {count}: Error: '{e}'."
                toWcfSerializationError f e

        let retVal =
            match trySerialize wcfSerializationFormat reply with
            | Ok r -> r
            | Error _ -> [||]

//        printfn $"tryReply - {count}: retVal.Length = {retVal.Length}."
        retVal


    /// Gets net tcp binding suitable for sending very large data objects.
    let getNetTcpBinding (security : WcfSecurityMode) =
        let binding = NetTcpBinding()
        binding.MaxReceivedMessageSize <- (int64 Int32.MaxValue)
        binding.MaxBufferPoolSize <- (int64 Int32.MaxValue)
        binding.MaxBufferSize <- Int32.MaxValue
        binding.OpenTimeout <- connectionTimeOut
        binding.CloseTimeout <- connectionTimeOut
        binding.SendTimeout <- dataTimeOut
        binding.ReceiveTimeout <- dataTimeOut
        binding.Security.Mode <- security.securityMode
        binding.ReaderQuotas <- getQuotas()
        binding


    /// Gets basic http binding suitable for sending very large data objects.
    let getBasicHttpBinding() =
        let binding = BasicHttpBinding()
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
            netTcpSecurityMode : WcfSecurityMode
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
                    netTcpSecurityMode = n.netTcpSecurityMode
                }
                |> Ok

            | (true, _), true, _ ->
                fail $"http service port: %A{h.httpServicePort} must be different from nettcp service port: %A{n.netTcpServicePort}"
            | (false, _), false, _ ->
                fail $"invalid IP address: %s{h.httpServiceAddress.value}"
            | (false, _), true, _ ->
                fail $"invalid IP address: %s{h.httpServiceAddress.value} and http service port: %A{h.httpServicePort} must be different from nettcp service port: %A{n.netTcpServicePort}"
            | _, _, false ->
                fail $"http IP address: %s{h.httpServiceAddress.value} and net tcp IP address: %s{n.netTcpServiceAddress.value} must be the same"

        static member defaultValue =
            {
                ipAddress = IPAddress.None
                httpPort = -1
                httpServiceName = String.Empty
                netTcpPort = - 1
                netTcpServiceName  = String.Empty
                netTcpSecurityMode = WcfSecurityMode.defaultValue
            }


    type WcfServiceProxy =
        {
            wcfLogger : WcfLogger
        }


    /// 'Data - is a type of initialization data that the service needs to operate.
    type WcfServiceData<'Data> =
        {
            wcfServiceAccessInfo : WcfServiceAccessInfo
            wcfServiceProxy : WcfServiceProxy
            serviceData : 'Data
            setData : 'Data -> unit
        }


    /// 'Service - is a type of the service itself.
    /// 'Data - is a type of initialization data that the service needs to operate.
    type WcfServiceData<'Service, 'Data>() =
        static let mutable serviceDataOpt :  WcfServiceData<'Data> option = None

        static member setData data =
            serviceDataOpt <- Some data
            data.setData data.serviceData

        static member tryGetData() = serviceDataOpt

        /// It is not really needed but we want to "use" the generic type to make the compiler happy.
        member _.serviceType = typeof<'Service>


    /// Tries to get a singleton data out of WcfServiceData and if failed, then uses a provided default value.
    let getData<'Service, 'Data> defaultValue =
        WcfServiceData<'Service, 'Data>.tryGetData()
        |> Option.bind (fun e -> Some e.serviceData)
        |> Option.defaultValue defaultValue


    /// See: https://github.com/CoreWCF/CoreWCF/issues/56
    ///
    /// 'Service - is a type of the WCF service itself.
    /// 'IWcfService - is a WCF interface that the service implements.
    /// 'Data - is a type of initialization data that the service needs to operate.
    ///
    /// Note that 'Service should have a constraint when 'Service : 'IWcfService.
    /// However, F# does not support this yet.
    type WcfStartup<'Service, 'IWcfService, 'Data when 'Service : not struct and 'IWcfService : not struct>() =
        let data = WcfServiceData<'Service, 'Data>.tryGetData()

        let createServiceModel (builder : IServiceBuilder) =
            match data with
            | Some d ->
                let i = d.wcfServiceAccessInfo
                builder
                    .AddService<'Service>()
                    .AddServiceEndpoint<'Service, 'IWcfService>(getBasicHttpBinding(), "/" + i.httpServiceName)
                    .AddServiceEndpoint<'Service, 'IWcfService>(getNetTcpBinding i.netTcpSecurityMode, "/" + i.netTcpServiceName)
                |> ignore
            | None -> invalidArg (nameof(data)) "Service data is missing."

        member _.ConfigureServices(services : IServiceCollection) =
            do
                services.AddServiceModelServices() |> ignore
                services.AddTransient<'Service>() |> ignore

        member _.Configure(app : IApplicationBuilder, env : IWebHostEnvironment) =
            do app.UseServiceModel(fun builder -> createServiceModel builder) |> ignore


    /// Wrapper around IWebHost to abstract it away and convert C# async methods into F# flavor.
    type WcfService(logger : WcfLogger, host : IWebHost) =
        let runTokenSource = new CancellationTokenSource()
        let stopTokenSource = new CancellationTokenSource()
        let shutDownTokenSource = new CancellationTokenSource()

        let logErr e =
            let err = e |> WcfExn
            err |> logger.logErrorData
            Error err

        let tryExecute g = tryExecute (fun () -> g() |> Ok) logErr

        member _.run() = tryExecute host.Run
        member _.runAsync() = async { do! host.RunAsync runTokenSource.Token |> Async.AwaitTask }
        member _.stop() = Task.Run(fun () -> host.StopAsync stopTokenSource.Token).Wait()
        member _.stopAsync() = async { do! host.StopAsync stopTokenSource.Token |> Async.AwaitTask }
        member _.waitForShutdown() = host.WaitForShutdown()
        member _.waitForShutdownAsync() = async { do! host.WaitForShutdownAsync shutDownTokenSource.Token |> Async.AwaitTask }

        member _.cancelRunAsync() = tryExecute runTokenSource.Cancel
        member _.cancelStopAsync() = tryExecute stopTokenSource.Cancel
        member _.cancelWaitForShutdownAsync() = tryExecute shutDownTokenSource.Cancel


    /// See: https://github.com/CoreWCF/CoreWCF/issues/56 for how to implement
    ///     [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall)]
    ///
    /// 'Service - is a type of the WCF service itself.
    /// 'IWcfService - is a WCF interface that the service implements.
    /// 'Data - is a type of initialization data that the service needs to operate.
    ///
    /// Note that 'Service should have a constraint when 'Service : 'IWcfService.
    /// However, F# does not support this yet.
    type WcfService<'Service, 'IWcfService, 'Data when 'Service : not struct and 'IWcfService : not struct>() =
        static let tryCreateWebHostBuilder (data : WcfServiceData<'Data> option) : WcfResult<WcfService> =
            match data with
            | Some d ->
                let info = d.wcfServiceAccessInfo
                let logger = d.wcfServiceProxy.wcfLogger
                try
                    logger.logInfoString $"ipAddress = %A{info.ipAddress}, httpPort = %A{info.httpPort}, netTcpPort = %A{info.netTcpPort}"
                    let endPoint = IPEndPoint(info.ipAddress, info.httpPort)

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
                            .UseStartup<WcfStartup<'Service, 'IWcfService, 'Data>>()
                            .Build()
                    (logger, host) |> WcfService |> Ok
                with
                | e -> WcfExn e |> toError
            | None -> WcfServiceCannotInitializeErr |> Error

        static let service : Lazy<WcfResult<WcfService>> =
            new Lazy<WcfResult<WcfService>>(fun () -> WcfServiceData<'Service, 'Data>.tryGetData() |> tryCreateWebHostBuilder)

        static member setData data = WcfServiceData<'Service, 'Data>.setData data
        static member tryGetService() = service.Value

        static member tryGetService data =
            printfn $"WcfService.tryGetService: data = %A{data}"
            WcfService<'Service, 'IWcfService, 'Data>.setData data
            service.Value


    let tryGetServiceData serviceAccessInfo wcfLogger serviceData =
        match WcfServiceAccessInfo.tryCreate serviceAccessInfo  with
        | Ok i ->
            {
                wcfServiceAccessInfo = i

                wcfServiceProxy =
                    {
                        wcfLogger = wcfLogger
                    }

                serviceData = serviceData
                setData = fun _ -> ()
            }
            |> Ok
        | Error e -> Error e
