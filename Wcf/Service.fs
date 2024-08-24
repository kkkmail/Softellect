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


    /// ! Do not try consolidating with Client. Different namespaces are used !
    /// This SecurityMode lives in CoreWCF namespace.
    type WcfSecurityMode
        with
        member s.securityMode =
            match s with
            | NoSecurity -> SecurityMode.None
            | TransportSecurity -> SecurityMode.Transport
            | MessageSecurity -> SecurityMode.Message
            | TransportWithMessageCredentialSecurity -> SecurityMode.TransportWithMessageCredential


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


    /// kk:20240824 - We'd want to have a constraint "and 'Service :> 'IWcfService" but F# does not allow it as of this date.
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
