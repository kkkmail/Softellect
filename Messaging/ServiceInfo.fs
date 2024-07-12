namespace Softellect.Messaging

open System.ServiceModel

open Softellect.Sys.Rop
open Softellect.Messaging.Primitives
open Softellect.Wcf.Common
open Softellect.Sys.Logging
open Softellect.Messaging.Errors

module ServiceInfo =

    type MsgResult<'T> = Result<'T, MessagingError>
    type MsgUnitResult = UnitResult<MessagingError>
    type MsgLogger = Logger<MessagingError>


    /// Client part of messaging service.
    type IMessagingClient<'D, 'E> =
        abstract getVersion : unit -> Result<MessagingDataVersion, 'E>
        abstract sendMessage : Message<'D> -> UnitResult<'E>
        abstract tryPeekMessage : MessagingClientId -> Result<Message<'D> option, 'E>
        abstract tryDeleteFromServer : (MessagingClientId * MessageId) -> UnitResult<'E>


    /// Server part of messaging service.
    type IMessagingService<'D, 'E> =
        abstract getVersion : unit -> Result<MessagingDataVersion, 'E>
        abstract sendMessage : Message<'D> -> UnitResult<'E>
        abstract tryPeekMessage : MessagingClientId -> Result<Message<'D> option, 'E>
        abstract tryDeleteFromServer : MessagingClientId * MessageId -> UnitResult<'E>
        abstract removeExpiredMessages : unit -> UnitResult<'E>


    /// Server WCF part of messaging service.
    /// The method removeExpiredMessages is not exposed via WCF.
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

        static member create dataVersion info =
            {
                messagingServiceAccessInfo = info
                messagingDataVersion = dataVersion
            }


    type MessagingClientAccessInfo =
        {
            msgClientId : MessagingClientId
            msgSvcAccessInfo : MessagingServiceAccessInfo
        }

        static member create dataVersion info clientId =
            {
                msgClientId = clientId
                msgSvcAccessInfo = MessagingServiceAccessInfo.create dataVersion info
            }
