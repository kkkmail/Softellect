namespace Softellect.DistributedProcessing.MessagingService

open Softellect.Messaging.AppSettings
open Softellect.Messaging.Proxy
open Softellect.Messaging.ServiceProxy
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.MessagingService.Program
open Softellect.Messaging.Service
open Softellect.Sys.Logging
open Softellect.DistributedProcessing.VersionInfo

module Program =

    let messagingServiceMain name args =
        let getLogger = fun _ -> Logger.defaultValue
        let serviceProxy :  MessagingServiceProxy<DistributedProcessingMessageData> = createMessagingServiceProxy getLogger messagingDataVersion
        let messagingServiceAccessInfo = loadMessagingServiceAccessInfo messagingDataVersion

        let data =
            {
                messagingServiceProxy = serviceProxy
                messagingServiceAccessInfo = messagingServiceAccessInfo
            }

        messagingMain<DistributedProcessingMessageData> name data args
