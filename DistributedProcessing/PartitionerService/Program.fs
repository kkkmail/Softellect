namespace Softellect.DistributedProcessing.PartitionerService

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.Messaging.Primitives
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.AppSettings.PartitionerService
open Softellect.Wcf.Program
open Softellect.DistributedProcessing.Proxy.PartitionerService
open Softellect.DistributedProcessing.Primitives.PartitionerService
open Softellect.DistributedProcessing.PartitionerService.Partitioner
open Softellect.DistributedProcessing.Messages
open Softellect.Sys.Logging
open Softellect.Messaging.ServiceProxy
open Softellect.Messaging.Client
open Softellect.DistributedProcessing.VersionInfo
open Softellect.Sys.AppSettings

module Program =

    let partitionerMain argv =
        setLogLevel()

        let partitionerServiceInfo : PartitionerServiceInfo = loadPartitionerServiceInfo messagingDataVersion
        let getMessageSize _ = SmallSize

        let messagingClientProxyInfo =
            {
                messagingClientId = partitionerServiceInfo.messagingClientAccessInfo.msgClientId
                messagingDataVersion = messagingDataVersion
                storageType = MsSqlDatabase
            }

        let msgClientProxy = createMessagingClientProxy<DistributedProcessingMessageData> getMessageSize messagingClientProxyInfo

        let messagingClientData =
            {
                msgAccessInfo = partitionerServiceInfo.messagingClientAccessInfo
                msgClientProxy = msgClientProxy
                logOnError = true
            }


        let proxy = PartitionerProxy.create partitionerServiceInfo

        let data =
            {
                partitionerServiceInfo = partitionerServiceInfo
                partitionerProxy = proxy
                messagingClientData = messagingClientData
            }

        let saveSettings() =
            // let result = updateMessagingServiceAccessInfo data.messagingServiceAccessInfo
            // Logger.logTrace (fun () -> $"saveSettings - result: '%A{result}'.")
            Logger.logCrit $"saveSettings - is not implemented yet."
            failwith "saveSettings - is not implemented yet."

        let configureServices (services : IServiceCollection) =
            let runner = PartitionerRunner(data)
            services.AddSingleton<IHostedService>(runner :> IHostedService) |> ignore

        let projectName = getProjectName() |> Some

        let postBuildHandler _ _ =
            Logger.logTrace (fun () -> $"partitionerMain - argv: %A{argv}.")
            Logger.logInfo $"partitionerMain - partitionerServiceInfo: %A{partitionerServiceInfo}."

        let programData =
            {
                serviceAccessInfo = data.partitionerServiceInfo.partitionerServiceAccessInfo
                getService = fun () -> PartitionerService(data.partitionerServiceInfo) :> IPartitionerService
                getWcfService = PartitionerWcfService
                saveSettings = saveSettings
                configureServices = Some configureServices
                configureServiceLogging = configureServiceLogging projectName
                configureLogging = configureLogging projectName
                postBuildHandler = Some postBuildHandler
            }

        wcfMain<IPartitionerService, IPartitionerWcfService, PartitionerWcfService> partitionerServiceProgramName programData argv
