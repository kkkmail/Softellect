namespace Softellect.Wcf

open Argu
open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Softellect.Sys.ExitErrorCodes
open Softellect.Wcf.Service
open Softellect.Wcf.Common
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

module Program =
    let x = 1

    // TODO kk:20240824 - Does not want to compile and seems too complicated with generics due to F# limitations. Delete in 180 days.


    //type ProgramData<'IService> =
    //    {
    //        serviceAccessInfo : ServiceAccessInfo
    //        getService : unit -> 'IService
    //    }


    //let private createHostBuilder<'IService, 'IWcfService when 'IService : not struct and 'IWcfService : not struct> (data : ProgramData<'IService>) =
    //    Host.CreateDefaultBuilder()
    //        .UseWindowsService()
    //        .ConfigureServices(fun hostContext services ->
    //            let service = data.getService()
    //            services.AddSingleton<'IService>(service) |> ignore)

    //        .ConfigureWebHostDefaults(fun webBuilder ->
    //            match data.serviceAccessInfo with
    //            | HttpServiceInfo i ->
    //                webBuilder.UseKestrel(fun options ->
    //                    let endPoint = IPEndPoint(i.httpServiceAddress.value.ipAddress, i.httpServicePort.value)
    //                    options.Listen(endPoint)
    //                    options.Limits.MaxResponseBufferSize <- (System.Nullable (int64 Int32.MaxValue))
    //                    options.Limits.MaxRequestBufferSize <- (System.Nullable (int64 Int32.MaxValue))
    //                    options.Limits.MaxRequestBodySize <- (System.Nullable (int64 Int32.MaxValue))) |> ignore
    //            | NetTcpServiceInfo i ->
    //                webBuilder.UseNetTcp(i.netTcpServicePort.value) |> ignore

    //            webBuilder.UseStartup(fun _ -> WcfStartup<'IService, 'IWcfService>(data.wcfServiceData)) |> ignore)


    //let main<'IService, 'IWcfService, 'Data, 'Args when 'IService : not struct and 'IWcfService : not struct and 'Args :> IArgParserTemplate> data argv =
    //    printfn $"main<{typeof<'D>.Name}> - data.messagingDataVersion = '{data.messagingDataVersion}'."
    //    printfn $"main<{typeof<'D>.Name}> - data.messagingServiceData = '{data.wcfServiceData.serviceData}'."
    //    printfn $"main<{typeof<'D>.Name}> - data.wcfServiceData = '{data.wcfServiceData}'."

    //    let runHost() = createHostBuilder<'IService, 'IWcfService, 'Data>(data).Build().Run()

    //    try
    //        let parser = ArgumentParser.Create<'Args>(programName = data.programName)

    //        match data.tryGetRunTask argv with
    //        | Some task -> task()
    //        | None ->  runHost()

    //        CompletedSuccessfully

    //    with
    //    | exn ->
    //        printfn $"%s{exn.Message}"
    //        UnknownException
