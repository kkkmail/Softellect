namespace Softellect.MessagingService

open Softellect.Sys.Logging
open Softellect.Sys.Errors
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Sys.WcfErrors
open Softellect.MessagingService.SvcCommandLine
open Softellect.Wcf.Service

//open ClmSys.MessagingData
//open MessagingServiceInfo.ServiceInfo
//open MessagingService.SvcCommandLine
//open ServiceProxy.MsgServiceProxy
//open DbData.Configuration
//open Primitives.VersionInfo

module ServiceImplementation =

    type MessagingService<'D> = MessagingService<'D, SoftellectError>
    type MessagingWcfService<'D> = MessagingWcfService<'D, SoftellectError>
    type MessagingWcfServiceImpl<'D> = WcfService<MessagingWcfService<'D>, IMessagingWcfService, MessagingServiceData<'D, SoftellectError>>


    //let mutable serviceSettings = getServiceSettings []


    //let tryCreateMessagingServiceData logger : WcfServiceDataResult<'D, 'E> =
    //    let i = getServiceSettings []

    //    let serviceData : MessagingServiceData =
    //        {
    //            messagingServiceInfo =
    //                {
    //                    expirationTime = i.messagingInfo.expirationTime
    //                    messagingDataVersion = messagingDataVersion
    //                }

    //            messagingServiceProxy = createMessagingServiceProxy getMessagingConnectionString
    //            communicationType = i.communicationType
    //        }

    //    let msgServiceDataRes = tryGetMsgServiceData i.messagingSvcInfo.messagingServiceAccessInfo logger serviceData
    //    msgServiceDataRes


    //let messagingServiceData = Lazy<WcfServiceDataResult<'D, 'E>>(fun () -> tryCreateMessagingServiceData (Logger.defaultValue))
