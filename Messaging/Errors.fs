namespace Softellect.Messaging

open System
open Softellect.Sys.Errors
open Softellect.Wcf.Errors
open Softellect.Messaging.Primitives

module Errors =

    type TryCreateMessageError =
        | TryCreateMessageDbErr of DbError
        | InvalidDeliveryTypeErr of MessageId * int
        | InvalidDataVersionErr of MessageId * VersionMismatchInfo
        | InvalidDeliveryTypeAndDataVersionErr of MessageId * int * VersionMismatchInfo


    type DeleteMessageError =
        | DeleteMessageDbErr of DbError
        | CannotDeleteMessageErr of MessageId


    type DeleteExpiredMessagesError =
        | DeleteExpiredMessagesDbErr of DbError


    type TryPickMessageError =
        | TryPickIncomingMessageDbErr of DbError
        | TryPickIncomingMessageWcfErr of WcfError
        | TryPickOutgoingMessageDbErr of DbError
        | TryPickOutgoingMessageWcfErr of WcfError
        //| UnableToLoadMessageErr of (MessagingClientId * MessageId)


    type SaveMessageError =
        | SaveMessageDbErr of DbError
        | CannotSaveMessageErr of MessageId


    type MessagingError =
        | AggregateErr of MessagingError * List<MessagingError>
        | TryCreateMessageErr of TryCreateMessageError
        | DeleteMessageErr of DeleteMessageError
        | TryPickMessageErr of TryPickMessageError
        | SaveMessageErr of SaveMessageError
        | DeleteExpiredMessagesErr of DeleteExpiredMessagesError


        static member private addError a b =
            match a, b with
            | AggregateErr (x, w), AggregateErr (y, z) -> AggregateErr (x, w @ (y :: z))
            | AggregateErr (x, w), _ -> AggregateErr (x, w @ [b])
            | _, AggregateErr (y, z) -> AggregateErr (a, y :: z)
            | _ -> AggregateErr (a, [b])

        static member (+) (a, b) = MessagingError.addError a b
        member a.add b = a + b



//module DataErrors =
//    type MessagingDbError =
//        | MessagingCannotDeleteMessageErr of MessageId


//module ClientErrors =

//    type GetVersionError =
//        | GetVersionWcfErr of WcfError
//        | VersionMismatchErr of VersionMismatchInfo


//    type OnGetMessagesError =
//        | ProcessedSuccessfullyWithInnerErr
//        | ProcessedWithErr
//        | ProcessedWithFailedToRemoveErr
//        | FailedToProcessErr
//        | BusyProcessingErr


//    //type SendMessageError =
//    //    | SendMessageFailedErr
//    //    | CannotDeleteMessageErr of MessageId


//    type TryReceiveSingleMessageError =
//        | TryPeekMessageErr
//        | SaveMessageErr
//        | TryDeleteFromServerErr


//    type SendMessageError =
//        | SendMessageWcfErr of WcfError
//        | CannotDeleteMessageErr of MessageId




//    type TryDeleteFromServerError =
//        | TryDeleteFromServerWcfErr of WcfError


//    //type MessageDeliveryError =
//    //    | ServiceNotStartedErr
//    //    | ServerIsShuttingDownErr
//    //    | DataVersionMismatchErr of MessagingDataVersion
//    //    | MsgWcfErr of WcfError


//    //type OnTryRemoveReceivedMessageError =
//    //    | MessageNotFoundErr of MessageId


//    type OnTryProcessMessageError =
//        | OnTryProcessMessageExn of exn


//    type MessagingClientError =
//        //| GeneralMessagingClientErr
//        | TimerEventErr of TimerEventError
//        | GetVersionErr of GetVersionError
//        | SendMessageErr of SendMessageError
//        | TryPeekMessageErr of TryPeekMessageError
//        | TryDeleteFromServerErr of TryDeleteFromServerError
//        //| SendMessageErr of SendMessageError
//        | TryReceiveSingleMessageErr of TryReceiveSingleMessageError
//        //| MessageDeliveryErr of MessageDeliveryError
//        //| OnTryRemoveReceivedMessageErr of OnTryRemoveReceivedMessageError
//        | OnTryProcessMessageErr of OnTryProcessMessageError


//module ServiceErrors =




//    type MsgSvcDbError =


//    type GetVersionSvcError =
//        | GetVersionSvcWcfErr of WcfError


//    type ConfigureServiceError =
//        | CfgSvcWcfErr of WcfError


////    type TryPeekMessageError =
////        | TryPeekMsgWcfErr of WcfError
////        | UnableToLoadMessageErr of (MessagingClientId * MessageId)


//    type MessageDeliveryError =
//        | ServiceNotStartedErr
//        | ServerIsShuttingDownErr
//        | DataVersionMismatchErr of MessagingDataVersion
//        | MsgWcfErr of WcfError


//    type TryDeleteFromServerError =
//        | TryDeleteMsgWcfErr of WcfError
//        | CannotFindClientErr of Guid
//        | UnableToDeleteMessageErr of (MessagingClientId * MessageId)
        
        
//    type MsgSettingsError =
//        | InvalidSettings of string
//        | MsgSettingExn of exn


//    type MessagingServiceError =
//        | InitializeErr of WcfError
//        | TimerEventErr of TimerEventError
//        | MsgSvcDbErr of MsgSvcDbError
//        | GetVersionSvcErr of GetVersionSvcError
//        | MessageDeliveryErr of MessageDeliveryError
//        | MessageUpsertErr of MessageUpsertError
//        | TryPeekMessageErr of TryPeekMessageError
//        | TryDeleteFromServerErr of TryDeleteFromServerError
//        | MsgSettingsErr of MsgSettingsError


//open ClientErrors
//open ServiceErrors

//module Errors =

//    type MessagingError =
//        //| MessagingDbErr of MessagingDbError
//        | MessagingClientErr of MessagingClientError
//        | MessagingServiceErr of MessagingServiceError
