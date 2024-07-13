namespace Softellect.Messaging

open System.ServiceModel

open Softellect.Sys.Rop
open Softellect.Messaging.Primitives
open Softellect.Wcf.Common
open Softellect.Sys.Logging
open Softellect.Messaging.Errors

module ServiceInfo =

    type MessagingResult<'D> = Result<'D, MessagingError>
    type MessagingOptionalResult<'D> = Result<Message<'D> option, MessagingError>
    type MessagingUnitResult = UnitResult<MessagingError>
    type MessagingLogger = Logger<MessagingError>
    type MessagingStateWithResult<'D> = 'D * MessagingUnitResult


    /// Client part of messaging service.
    type IMessagingClient<'D> =
        abstract getVersion : unit -> MessagingResult<MessagingDataVersion>
        abstract sendMessage : Message<'D> -> MessagingUnitResult
        abstract tryPickMessage : MessagingClientId -> MessagingOptionalResult<'D>
        abstract tryDeleteFromServer : (MessagingClientId * MessageId) -> MessagingUnitResult


    /// Server part of messaging service.
    type IMessagingService<'D> =
        abstract getVersion : unit -> MessagingResult<MessagingDataVersion>
        abstract sendMessage : Message<'D> -> MessagingUnitResult
        abstract tryPickMessage : MessagingClientId -> MessagingOptionalResult<'D>
        abstract tryDeleteFromServer : MessagingClientId * MessageId -> MessagingUnitResult
        abstract removeExpiredMessages : unit -> MessagingUnitResult


    /// Server WCF part of messaging service.
    /// The method removeExpiredMessages is not exposed via WCF.
    /// https://gist.github.com/dgfitch/661656
    [<ServiceContract(ConfigurationName = MessagingWcfServiceName)>]
    type IMessagingWcfService =

        [<OperationContract(Name = "getVersion")>]
        abstract getVersion : u:byte[] -> byte[]

        [<OperationContract(Name = "sendMessage")>]
        abstract sendMessage : m:byte[] -> byte[]

        [<OperationContract(Name = "tryPickMessage")>]
        abstract tryPickMessage : c:byte[] -> byte[]

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
