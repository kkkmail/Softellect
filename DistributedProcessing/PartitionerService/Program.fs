namespace Softellect.DistributedProcessing.PartitionerService

open Argu
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.Messaging.Primitives
open Softellect.DistributedProcessing.Primitives.Common
//open Softellect.DistributedProcessing.PartitionerService.Worker
open Softellect.DistributedProcessing.PartitionerService.CommandLine
//open Softellect.DistributedProcessing.PartitionerService.Partitioner
open Softellect.DistributedProcessing.AppSettings.PartitionerService
open Softellect.Sys.ExitErrorCodes
open Softellect.Wcf.Program
open Softellect.DistributedProcessing.Proxy.PartitionerService
open Softellect.DistributedProcessing.Primitives.PartitionerService
open Softellect.DistributedProcessing.PartitionerService.Partitioner
open Softellect.DistributedProcessing.DataAccess.PartitionerService
open Softellect.Sys.Logging
open Softellect.Messaging.ServiceProxy
open Softellect.Messaging.Client
open Softellect.DistributedProcessing.VersionInfo

module Program =

    let partitionerMain argv =
        printfn $"partitionerMain - argv: %A{argv}."
        let partitionerServiceInfo : PartitionerServiceInfo = loadPartitionerServiceInfo messagingDataVersion
        let getLogger = fun _ -> Logger.defaultValue
        let getMessageSize _ = SmallSize

        let messagingClientProxyInfo =
            {
                messagingClientId = partitionerServiceInfo.messagingClientAccessInfo.msgClientId
                messagingDataVersion = messagingDataVersion
                storageType = MsSqlDatabase
            }

        let msgClientProxy = createMessagingClientProxy<DistributedProcessingMessageData> getLogger getMessageSize messagingClientProxyInfo

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
            //let result = updateMessagingServiceAccessInfo data.messagingServiceAccessInfo
            //printfn $"saveSettings - result: '%A{result}'."
            failwith ""

        let configureServices (services : IServiceCollection) =
            let runner = new PartitionerRunner(data)
            services.AddSingleton<IHostedService>(runner :> IHostedService) |> ignore

        let programData =
            {
                serviceAccessInfo = data.partitionerServiceInfo.partitionerServiceAccessInfo
                getService = fun () -> new PartitionerService(data.partitionerServiceInfo) :> IPartitionerService
                getWcfService = fun service -> new PartitionerWcfService(service)
                saveSettings = saveSettings
                configureServices = Some configureServices
            }

        printfn $"partitionerMain - partitionerServiceInfo: %A{partitionerServiceInfo}."
        wcfMain<IPartitionerService, IPartitionerWcfService, PartitionerWcfService> partitionerServiceProgramName programData argv
