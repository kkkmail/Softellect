namespace Softellect.Messaging

open System.ServiceModel

open Softellect.Sys.Rop
open Softellect.Messaging.Primitives
open Softellect.Wcf.Common
open Softellect.Sys.Logging
open Softellect.Messaging.Errors

module ServiceInfo =

    //// ===================================================
    //
    //// Double gneric to send structured errors back.
    //
    //type MessagingResult<'D, 'E> = Result<'D, 'E>
    //type MessagingOptionalResult<'D, 'E> = Result<Message<'D> option, 'E>
    //type MessagingUnitResult<'E> = UnitResult<'E>
    ////type MessagingLogger = Logger<MessagingError>
    //type MessagingStateWithResult<'D, 'E> = 'D * MessagingUnitResult<'E>
    //
    //
    ///// Client part of messaging service.
    //type IMessagingClient<'D, 'E> =
    //    abstract getVersion : unit -> MessagingResult<MessagingDataVersion, 'E>
    //    abstract sendMessage : Message<'D> -> MessagingUnitResult<'E>
    //    abstract tryPickMessage : MessagingClientId -> MessagingOptionalResult<'D, 'E>
    //    abstract tryDeleteFromServer : (MessagingClientId * MessageId) -> MessagingUnitResult<'E>
    //
    //
    ///// Server part of messaging service.
    //type IMessagingService<'D, 'E> =
    //    abstract getVersion : unit -> MessagingResult<MessagingDataVersion, 'E>
    //    abstract sendMessage : Message<'D> -> MessagingUnitResult<'E>
    //    abstract tryPickMessage : MessagingClientId -> MessagingOptionalResult<'D, 'E>
    //    abstract tryDeleteFromServer : MessagingClientId * MessageId -> MessagingUnitResult<'E>
    //    abstract removeExpiredMessages : unit -> MessagingUnitResult<'E>
    //
    //// ===================================================
    ////
    //// Singe gneric when capturing structured error on the other side is not needed.
    //
    //type MessagingResult<'D> = MessagingResult<'D, MessagingError>
    //type MessagingOptionalResult<'D> = MessagingOptionalResult<'D, MessagingError>
    //type MessagingUnitResult = UnitResult<MessagingError>
    //type MessagingLogger = Logger<MessagingError>
    //type MessagingStateWithResult<'D> = MessagingStateWithResult<'D, MessagingError>
    //
    //
    ///// Client part of messaging service.
    //type IMessagingClient<'D> = IMessagingClient<'D, MessagingError>
    //
    //
    ///// Server part of messaging service.
    //type IMessagingService<'D> = IMessagingService<'D, MessagingError>
    //
    // ===================================================

    /// 'D is the strongly typed data that is being sent / received by messaging service.
    type MessagingResult<'D> = Result<'D, MessagingError>
    type MessagingOptionalResult<'D> = Result<Message<'D> option, MessagingError>
    type MessagingUnitResult = UnitResult<MessagingError>
    type MessagingLogger = Logger<MessagingError>
    type MessagingStateWithResult<'D> = 'D * MessagingUnitResult


    // Client part of messaging service.
    type IMessagingClient<'D> =
        abstract getVersion : unit -> MessagingResult<MessagingDataVersion>
        abstract sendMessage : Message<'D> -> MessagingUnitResult
        abstract tryPickMessage : MessagingClientId -> MessagingOptionalResult<'D>
        abstract tryDeleteFromServer : (MessagingClientId * MessageId) -> MessagingUnitResult


    // Server part of messaging service.
    type IMessagingService<'D> =
        abstract tryStart : unit -> MessagingUnitResult
        abstract getVersion : unit -> MessagingResult<MessagingDataVersion>
        abstract sendMessage : Message<'D> -> MessagingUnitResult
        abstract tryPickMessage : MessagingClientId -> MessagingOptionalResult<'D>
        abstract tryDeleteFromServer : MessagingClientId * MessageId -> MessagingUnitResult
        abstract removeExpiredMessages : unit -> MessagingUnitResult

    // ===================================================
    //
    // Common part.

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
