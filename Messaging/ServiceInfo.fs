namespace Softellect.Messaging

open System.ServiceModel

open Softellect.Sys.MessagingPrimitives
open Softellect.Messaging.Primitives
open Softellect.Wcf.Common

module ServiceInfo =

    /// Client part of messaging service.
    type IMessagingService<'D> =
        abstract getVersion : unit -> MsgResult<MessagingDataVersion>
        abstract sendMessage : Message<'D> -> MsgUnitResult
        abstract tryPeekMessage : MessagingClientId -> MsgResult<Message<'D> option>
        abstract tryDeleteFromServer : (MessagingClientId * MessageId) -> MsgUnitResult


    /// WCF part of messaging service.
    /// https://gist.github.com/dgfitch/661656
    [<ServiceContract(ConfigurationName = MessagingWcfServiceName)>]
    type IMessagingWcfService =

        [<OperationContract(Name = "getVersion")>]
        abstract getVersion : u:byte[] -> byte[]

        [<OperationContract(Name = "sendMessage")>]
        abstract sendMessage : m:byte[] -> byte[]

        [<OperationContract(Name = "tryPeekMessage")>]
        abstract tryPeekMessage : c:byte[] -> byte[]

        [<OperationContract(Name = "tryDeleteFromServer")>]
        abstract tryDeleteFromServer : cm:byte[] -> byte[]


    type MessagingServiceAccessInfo =
        {
            messagingServiceAccessInfo : ServiceAccessInfo
            messagingDataVersion : MessagingDataVersion
        }


    type MessagingClientAccessInfo =
        {
            msgClientId : MessagingClientId
            msgSvcAccessInfo : MessagingServiceAccessInfo
        }
