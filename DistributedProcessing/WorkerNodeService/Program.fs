namespace Softellect.DistributedProcessing.WorkerNodeService

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.Messaging.Primitives
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.WorkerNodeService.Worker
open Softellect.DistributedProcessing.WorkerNodeService.WorkerNode
open Softellect.DistributedProcessing.AppSettings.WorkerNodeService
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.WindowsApi
open Softellect.Wcf.Program
open Softellect.DistributedProcessing.Proxy.WorkerNodeService
open Softellect.DistributedProcessing.Primitives.WorkerNodeService
open Softellect.DistributedProcessing.Messages
open Softellect.Messaging.ServiceProxy
open Softellect.Messaging.Client
open Softellect.DistributedProcessing.VersionInfo
open Softellect.Sys.AppSettings
open Softellect.Sys.Core

module Program =

    let workerNodeMain argv =
        setLogLevel()
        let workerNodeServiceInfo = loadWorkerNodeServiceInfo messagingDataVersion
        let getMessageSize _ = SmallSize

        let messagingClientProxyInfo =
            {
                messagingClientId = workerNodeServiceInfo.messagingClientAccessInfo.msgClientId
                messagingDataVersion = messagingDataVersion
                storageType = MsSqlDatabase
            }

        let msgClientProxy = createMessagingClientProxy<DistributedProcessingMessageData> getMessageSize messagingClientProxyInfo

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
            //Logger.logCrit $"saveSettings - result: '%A{result}'."
            Logger.logCrit $"saveSettings is not implemented yet'."
            failwith "saveSettings is not implemented yet."

        let configureServices (services : IServiceCollection) =
            let runner = WorkerNodeRunner(data)
            services.AddSingleton<IHostedService>(runner :> IHostedService) |> ignore

        let projectName = getProjectName() |> Some

        let postBuildHandler _ _ =
            Logger.logInfo $"workerNodeMain - workerNodeServiceInfo: %A{workerNodeServiceInfo}."
            checkMonitorData()

            // match tryChangeResolution MonitorResolution.fullHD with
            // | Ok() -> ()
            // | Error e -> Logger.logError $"%A{e}"
            //
            // checkMonitorData()

        let programData =
            {
                serviceAccessInfo = data.workerNodeServiceInfo.workerNodeServiceAccessInfo
                getService = fun () -> WorkerNodeService(data.workerNodeServiceInfo) :> IWorkerNodeService
                getWcfService = fun service -> WorkerNodeWcfService(service)
                saveSettings = saveSettings
                configureServices = Some configureServices
                configureServiceLogging = configureServiceLogging projectName
                configureLogging = configureLogging projectName
                postBuildHandler = Some postBuildHandler
            }


        wcfMain<IWorkerNodeService, IWorkerNodeWcfService, WorkerNodeWcfService> workerNodeServiceProgramName programData argv
