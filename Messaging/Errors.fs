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


    type OnGetMessagesError =
        | ProcessedSuccessfullyWithInnerErr
        | ProcessedWithErr
        | ProcessedWithFailedToRemoveErr
        | FailedToProcessErr
        | BusyProcessingErr


    type GetVersionSvcError =
        | GetVersionSvcWcfErr of WcfError


    type MessageDeliveryError =
        //| ServiceNotStartedErr
        //| ServerIsShuttingDownErr
        //| DataVersionMismatchErr of MessagingDataVersion
        | MsgWcfErr of WcfError


    type TryPickMessageWcfError =
        | TryPickMsgWcfErr of WcfError
        //| UnableToLoadMessageErr of (MessagingClientId * MessageId)


    //type TryDeleteFromServerWcfError =
    //    | TryDeleteFromServerWcfErr of WcfError


    type TryDeleteFromServerError =
        //| TryDeleteMsgWcfErr of WcfError
        | TryDeleteFromServerWcfErr of WcfError
//        | CannotFindClientErr of Guid
//        | UnableToDeleteMessageErr of (MessagingClientId * MessageId)


    type MsgSettingsError =
        | InvalidSettings of string
        | MsgSettingExn of exn


    type TryReceiveSingleMessageError =
        | TryReceiveSingleMessagePickErr
        | TryReceiveSingleMessageSaveErr
        | TryReceiveSingleMessageDeleteErr


    type GetVersionError =
        | GetVersionWcfErr of WcfError
        //| VersionMismatchErr of VersionMismatchInfo


    type SendMessageError =
        | SendMessageWcfErr of WcfError
        //| CannotDeleteMessageErr of MessageId


    type OnTryProcessMessageError =
        | OnTryProcessMessageExn of exn


    type MessagingError =
        | AggregateErr of MessagingError * List<MessagingError>
        | TryCreateMessageErr of TryCreateMessageError
        | DeleteMessageErr of DeleteMessageError
        | TryPickMessageErr of TryPickMessageError
        | SaveMessageErr of SaveMessageError
        | DeleteExpiredMessagesErr of DeleteExpiredMessagesError
        | OnGetMessagesErr of OnGetMessagesError
        | TimerEventErr of TimerEventError
        | GetVersionSvcErr of GetVersionSvcError
        | MessageDeliveryErr of MessageDeliveryError
        | TryPickMessageWcfErr of TryPickMessageWcfError
        | TryDeleteFromServerErr of TryDeleteFromServerError
        | MsgSettingsErr of MsgSettingsError
        | TryReceiveSingleMessageErr of TryReceiveSingleMessageError
        | GetVersionErr of GetVersionError
        | SendMessageErr of SendMessageError
        | OnTryProcessMessageErr of OnTryProcessMessageError

//        | TryPeekMessageErr of TryPeekMessageError
//        | TryDeleteFromServerErr of TryDeleteFromServerError


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


//    //type OnTryRemoveReceivedMessageError =
//    //    | MessageNotFoundErr of MessageId


//    type MessagingClientError =
//        //| GeneralMessagingClientErr
//        | TimerEventErr of TimerEventError
//        //| SendMessageErr of SendMessageError
//        //| MessageDeliveryErr of MessageDeliveryError
//        //| OnTryRemoveReceivedMessageErr of OnTryRemoveReceivedMessageError


//module ServiceErrors =




//    type MsgSvcDbError =


//    type ConfigureServiceError =
//        | CfgSvcWcfErr of WcfError


//    type MessageDeliveryError =
//        | ServiceNotStartedErr
//        | ServerIsShuttingDownErr
//        | DataVersionMismatchErr of MessagingDataVersion
//        | MsgWcfErr of WcfError


//    type MessagingServiceError =
//        | InitializeErr of WcfError
//        | TimerEventErr of TimerEventError
//        | MsgSvcDbErr of MsgSvcDbError
//        | MessageUpsertErr of MessageUpsertError


//open ClientErrors
//open ServiceErrors

//module Errors =

//    type MessagingError =
//        //| MessagingDbErr of MessagingDbError
//        | MessagingClientErr of MessagingClientError
//        | MessagingServiceErr of MessagingServiceError
