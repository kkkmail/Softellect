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

    //type ProgramData<'IService, 'Data> =
    //    {
    //        dataVersion : DataVersion
    //        programName : string
    //        wcfServiceData : WcfServiceData<'Data>
    //        getService : 'Data -> 'IService
    //        tryGetRunTask : string -> (unit -> unit) option
    //    }


    //let private createHostBuilder<'IService, 'IWcfService, 'Data when 'IService : not struct and 'IWcfService : not struct> (data : ProgramData<'IService, 'Data>) =
    //    Host.CreateDefaultBuilder()
    //        .UseWindowsService()
    //        .ConfigureServices(fun hostContext services ->
    //            let service = data.getService(data.wcfServiceData.serviceData)
    //            services.AddSingleton<'IService>(service) |> ignore)

    //        .ConfigureWebHostDefaults(fun webBuilder ->
    //            match data.wcfServiceData.wcfServiceAccessInfo with
    //            | HttpServiceInfo i ->
    //                webBuilder.UseKestrel(fun options ->
    //                    let endPoint = IPEndPoint(i.httpServiceAddress.value.ipAddress, i.httpServicePort.value)
    //                    options.Listen(endPoint)
    //                    options.Limits.MaxResponseBufferSize <- (System.Nullable (int64 Int32.MaxValue))
    //                    options.Limits.MaxRequestBufferSize <- (System.Nullable (int64 Int32.MaxValue))
    //                    options.Limits.MaxRequestBodySize <- (System.Nullable (int64 Int32.MaxValue))) |> ignore
    //            | NetTcpServiceInfo i ->
    //                webBuilder.UseNetTcp(i.netTcpServicePort.value) |> ignore

    //            webBuilder.UseStartup(fun _ -> WcfStartup<'IService, 'IWcfService, 'Data>(data.wcfServiceData)) |> ignore)


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
