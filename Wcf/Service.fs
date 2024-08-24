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
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

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


    //type WcfServiceAccessInfo =
    //    {
    //        ipAddress : IPAddress
    //        httpPort : int
    //        httpServiceName : string
    //        netTcpPort : int
    //        netTcpServiceName : string
    //        netTcpSecurityMode : WcfSecurityMode
    //    }

    //    static member tryCreate (i : ServiceAccessInfo) =
    //        let fail e : WcfResult<WcfServiceAccessInfo> = e |> WcfCriticalErr |> Error
    //        let h = i.httpServiceInfo
    //        let n = i.netTcpServiceInfo

    //        match IPAddress.TryParse h.httpServiceAddress.value, h.httpServicePort = n.netTcpServicePort, h.httpServiceAddress = n.netTcpServiceAddress with
    //        | (true, ipAddress), false, true ->
    //            {
    //                ipAddress = ipAddress
    //                httpPort = h.httpServicePort.value
    //                httpServiceName = h.httpServiceName.value
    //                netTcpPort = n.netTcpServicePort.value
    //                netTcpServiceName = n.netTcpServiceName.value
    //                netTcpSecurityMode = n.netTcpSecurityMode
    //            }
    //            |> Ok

    //        | (true, _), true, _ ->
    //            fail $"http service port: %A{h.httpServicePort} must be different from nettcp service port: %A{n.netTcpServicePort}"
    //        | (false, _), false, _ ->
    //            fail $"invalid IP address: %s{h.httpServiceAddress.value}"
    //        | (false, _), true, _ ->
    //            fail $"invalid IP address: %s{h.httpServiceAddress.value} and http service port: %A{h.httpServicePort} must be different from nettcp service port: %A{n.netTcpServicePort}"
    //        | _, _, false ->
    //            fail $"http IP address: %s{h.httpServiceAddress.value} and net tcp IP address: %s{n.netTcpServiceAddress.value} must be the same"

    //    static member defaultValue =
    //        {
    //            ipAddress = IPAddress.None
    //            httpPort = -1
    //            httpServiceName = String.Empty
    //            netTcpPort = - 1
    //            netTcpServiceName  = String.Empty
    //            netTcpSecurityMode = WcfSecurityMode.defaultValue
    //        }


    //type WcfServiceProxy =
    //    {
    //        wcfLogger : WcfLogger
    //    }


    ///// 'Data - is a type of initialization data that the underlying service needs to operate.
    //type WcfServiceData<'Data> =
    //    {
    //        //wcfServiceProxy : WcfServiceProxy
    //        serviceAccessInfo : ServiceAccessInfo
    //        serviceData : 'Data
    //    }


    type WcfStartup<'Service, 'IWcfService when 'Service : not struct and 'IWcfService : not struct>(d : ServiceAccessInfo) =
        let createServiceModel (builder : IServiceBuilder) =
            let w (b : IServiceBuilder) =
                match d with
                | HttpServiceInfo i ->
                    let httpBinding = getBasicHttpBinding()
                    printfn $"createServiceModel - httpBinding: '{httpBinding}', httpServiceName: '{i.httpServiceName}'."
                    b.AddServiceEndpoint<'Service, 'IWcfService>(httpBinding, "/" + i.httpServiceName.value)
                | NetTcpServiceInfo i ->
                    let netTcpBinding = getNetTcpBinding i.netTcpSecurityMode
                    printfn $"createServiceModel - netTcpBinding: '{netTcpBinding}', netTcpServiceName: '{i.netTcpServiceName}'."
                    b.AddServiceEndpoint<'Service, 'IWcfService>(netTcpBinding, "/" + i.netTcpServiceName.value)

            builder
                .AddService<'Service>()
                |> w
            |> ignore

        member _.ConfigureServices(services : IServiceCollection) =
            do
                services.AddServiceModelServices() |> ignore
                services.AddTransient<'Service>() |> ignore

        member _.Configure(app : IApplicationBuilder, env : IWebHostEnvironment) =
            do app.UseServiceModel(fun builder -> createServiceModel builder) |> ignore


    //let getServiceData serviceAccessInfo serviceData =
    //    {
    //        //wcfServiceProxy =
    //        //    {
    //        //        wcfLogger = wcfLogger
    //        //    }

    //        serviceAccessInfo = serviceAccessInfo
    //        serviceData = serviceData
    //    }
