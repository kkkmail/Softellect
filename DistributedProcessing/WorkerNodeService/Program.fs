namespace Softellect.DistributedProcessing.WorkerNodeService

open Argu
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.Messaging.Primitives
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.WorkerNodeService.Worker
open Softellect.DistributedProcessing.WorkerNodeService.CommandLine
open Softellect.DistributedProcessing.WorkerNodeService.WorkerNode
open Softellect.DistributedProcessing.AppSettings.WorkerNodeService
open Softellect.Sys.ExitErrorCodes
open Softellect.Wcf.Program
open Softellect.DistributedProcessing.Proxy.WorkerNodeService
open Softellect.DistributedProcessing.WorkerNodeService.Primitives
open Softellect.Sys.Logging
open Softellect.Messaging.ServiceProxy
open Softellect.Messaging.Client
open Softellect.DistributedProcessing.VersionInfo

module Program =

    //type WorkerNodeProgramData<'D, 'P> =
    //    {
    //        x : int
    //        y : DistributedProcessingMessageData<'D, 'P>
    //    }

    //let private createHostBuilder<'D, 'P> (v : MessagingDataVersion) =
    //    Host.CreateDefaultBuilder()
    //        .UseWindowsService()
    //        .ConfigureServices(fun hostContext services ->
    //            services.AddSingleton(v) |> ignore
    //            services.AddHostedService<MsgWorker<'D>>() |> ignore)


    //let main<'D, 'P> workerNodeProgramName data argv =
    //    //let runHost() = createHostBuilder<'D>(v).Build().Run()

    //    //try
    //    //    let parser = ArgumentParser.Create<MsgSvcArguArgs>(programName = messagingProgramName)
    //    //    let results = (parser.Parse argv).GetAllResults() |> MsgSvcArgs.fromArgu convertArgs

    //    //    let run p =
    //    //        getParams v p |> ignore
    //    //        runHost

    //    //    match MessagingServiceTask.tryCreate run (getSaveSettings v) results with
    //    //    | Some task -> task.run()
    //    //    | None ->  runHost()

    //    //    CompletedSuccessfully

    //    //with
    //    //| exn ->
    //    //    printfn $"%s{exn.Message}"
    //    //    UnknownException
    //    0


    let workerNodeMain argv =
        let workerNodeServiceInfo = loadWorkerNodeServiceInfo messagingDataVersion
        let getLogger = fun _ -> Logger.defaultValue
        let getMessageSize _ = SmallSize

        let messagingClientProxyInfo =
            {
                messagingClientId = workerNodeServiceInfo.messagingClientAccessInfo.msgClientId
                messagingDataVersion = messagingDataVersion
                storageType = MsSqlDatabase
            }

        let msgClientProxy = createMessagingClientProxy<DistributedProcessingMessageData> getLogger getMessageSize messagingClientProxyInfo

        let messagingClientData =
            {
                msgAccessInfo = workerNodeServiceInfo.messagingClientAccessInfo
                msgClientProxy = msgClientProxy
                logOnError = true
            }


        let proxy = WorkerNodeProxy.create workerNodeServiceInfo.workerNodeLocalInto

        let data =
            {
                workerNodeServiceInfo = workerNodeServiceInfo
                workerNodeProxy = proxy
                messagingClientData = messagingClientData
            }

        let saveSettings() =
            //let result = updateMessagingServiceAccessInfo data.messagingServiceAccessInfo
            //printfn $"saveSettings - result: '%A{result}'."
            failwith "saveSettings is not implemented yet."

        let configureServices (services : IServiceCollection) =
            let runner = new WorkerNodeRunner(data)
            services.AddSingleton<IHostedService>(runner :> IHostedService) |> ignore

        let programData =
            {
                serviceAccessInfo = data.workerNodeServiceInfo.workerNodeServiceAccessInfo
                getService = fun () -> new WorkerNodeService(data.workerNodeServiceInfo) :> IWorkerNodeService
                getWcfService = fun service -> new WorkerNodeWcfService(service)
                saveSettings = saveSettings
                configureServices = Some configureServices
            }

        printfn $"workerNodeMain - workerNodeServiceInfo: %A{workerNodeServiceInfo}."
        wcfMain<IWorkerNodeService, IWorkerNodeWcfService, WorkerNodeWcfService> workerNodeServiceProgramName programData argv
