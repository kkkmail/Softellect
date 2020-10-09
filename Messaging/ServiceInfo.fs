namespace Softellect.Messaging

open System
open System.ServiceModel

open Softellect.Sys.Errors
open Softellect.Sys.MessagingPrimitives
open Softellect.Messaging.Primitives
open Softellect.Wcf.Common
open Softellect.Wcf.Client

module ServiceInfo =

    /// Client part of messaging service
    type IMessagingClient<'D, 'E> =
        abstract getVersion : unit -> ResultWithErr<MessagingDataVersion, 'E>
        abstract sendMessage : Message<'D> -> UnitResult<'E>
        abstract tryPeekMessage : MessagingClientId -> ResultWithErr<Message<'D> option, 'E>
        abstract tryDeleteFromServer : (MessagingClientId * MessageId) -> UnitResult<'E>


    type IMessagingService<'D, 'E> =
        abstract getVersion : unit -> ResultWithErr<MessagingDataVersion, 'E>
        abstract sendMessage : Message<'D> -> UnitResult<'E>
        abstract tryPeekMessage : MessagingClientId -> ResultWithErr<Message<'D> option, 'E>
        abstract tryDeleteFromServer : (MessagingClientId * MessageId) -> UnitResult<'E>


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
