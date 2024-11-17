namespace Softellect.DistributedProcessing.WorkerNodeService

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.Messaging.Primitives
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.WorkerNodeService.Worker
open Softellect.DistributedProcessing.WorkerNodeService.WorkerNode
open Softellect.DistributedProcessing.AppSettings.WorkerNodeService
open Softellect.Wcf.Program
open Softellect.DistributedProcessing.Proxy.WorkerNodeService
open Softellect.DistributedProcessing.Primitives.WorkerNodeService
open Softellect.Sys.Logging
open Softellect.Messaging.ServiceProxy
open Softellect.Messaging.Client
open Softellect.DistributedProcessing.VersionInfo

module Program =

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


        let proxy = WorkerNodeProxy.create workerNodeServiceInfo

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
