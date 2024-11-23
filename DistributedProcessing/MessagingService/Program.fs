namespace Softellect.DistributedProcessing.MessagingService

open Softellect.Messaging.AppSettings
open Softellect.Messaging.Proxy
open Softellect.Messaging.ServiceProxy
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.MessagingService.Program
open Softellect.Messaging.Service
open Softellect.Sys.Logging
open Softellect.DistributedProcessing.VersionInfo
open Softellect.Sys.AppSettings

module Program =

    let messagingServiceMain name args =
        setLogLevel()
        let serviceProxy :  MessagingServiceProxy<DistributedProcessingMessageData> = createMessagingServiceProxy messagingDataVersion
        let messagingServiceAccessInfo = loadMessagingServiceAccessInfo messagingDataVersion

        let data =
            {
                messagingServiceProxy = serviceProxy
                messagingServiceAccessInfo = messagingServiceAccessInfo
            }

        messagingMain<DistributedProcessingMessageData> name data args
