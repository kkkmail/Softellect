namespace Softellect.Sys

open System
open TimerErrors
open WcfErrors
open MessagingPrimitives

module MessagingClientErrors =

    type GetVersionError =
        | GetVersionWcfErr of WcfError
        //| VersionMismatchErr of VersionMismatchInfo


    type OnGetMessagesError =
        | ProcessedSuccessfullyWithInnerErr
        | ProcessedWithErr
        | ProcessedWithFailedToRemoveErr
        | FailedToProcessErr
        | BusyProcessingErr


    //type SendMessageError =
    //    | SendMessageFailedErr
    //    | CannotDeleteMessageErr of MessageId


    type TryReceiveSingleMessageError =
        | TryPeekMessageErr
        | SaveMessageErr
        | TryDeleteFromServerErr


    type SendMessageError =
        | SendMessageWcfErr of WcfError


    type TryPeekMessageError =
        | TryPeekMessageWcfErr of WcfError


    type TryDeleteFromServerError =
        | TryDeleteFromServerWcfErr of WcfError


    //type MessageDeliveryError =
    //    | ServiceNotStartedErr
    //    | ServerIsShuttingDownErr
    //    | DataVersionMismatchErr of MessagingDataVersion
    //    | MsgWcfErr of WcfError


    //type OnTryRemoveReceivedMessageError =
    //    | MessageNotFoundErr of MessageId


    type OnTryProcessMessageError =
        | OnTryProcessMessageExn of exn


    type MessagingClientError =
        //| GeneralMessagingClientErr
        | TimerEventErr of TimerEventError
        | GetVersionErr of GetVersionError
        | SendMessageErr of SendMessageError
        | TryPeekMessageErr of TryPeekMessageError
        | TryDeleteFromServerErr of TryDeleteFromServerError
        //| SendMessageErr of SendMessageError
        | TryReceiveSingleMessageErr of TryReceiveSingleMessageError
        //| MessageDeliveryErr of MessageDeliveryError
        //| OnTryRemoveReceivedMessageErr of OnTryRemoveReceivedMessageError
        | OnTryProcessMessageErr of OnTryProcessMessageError


module MessagingServiceErrors =

    type MessageCreateError =
        | InvalidDeliveryTypeErr of int
        | InvalidDataVersionErr of VersionMismatchInfo
        | InvalidDeliveryTypeAndDataVersionErr of int * VersionMismatchInfo


    type MessageUpsertError =
        | CannotUpsertMessageErr of MessageId


    type MessageDeleteError =
        | CannotDeleteMessageErr of MessageId


    type MsgSvcDbError =
        | MessageCreateErr of MessageCreateError
        | MessageDeleteErr of MessageDeleteError


    type GetVersionSvcError =
        | GetVersionSvcWcfErr of WcfError


    type ConfigureServiceError =
        | CfgSvcWcfErr of WcfError


    type TryPeekMessageError =
        | TryPeekMsgWcfErr of WcfError
        | UnableToLoadMessageErr of (MessagingClientId * MessageId)


    type MessageDeliveryError =
        | ServiceNotStartedErr
        | ServerIsShuttingDownErr
        | DataVersionMismatchErr of MessagingDataVersion
        | MsgWcfErr of WcfError


    type TryDeleteFromServerError =
        | TryDeleteMsgWcfErr of WcfError
        | CannotFindClientErr of Guid
        | UnableToDeleteMessageErr of (MessagingClientId * MessageId)
        
        
    type MsgSettingsError =
        | InvalidSettings of string
        | MsgSettingExn of exn


    type MessagingServiceError =
        | InitializeErr of WcfError
        | TimerEventErr of TimerEventError
        | MsgSvcDbErr of MsgSvcDbError
        | GetVersionSvcErr of GetVersionSvcError
        | MessageDeliveryErr of MessageDeliveryError
        | MessageUpsertErr of MessageUpsertError
        | TryPeekMessageErr of TryPeekMessageError
        | TryDeleteFromServerErr of TryDeleteFromServerError
        | MsgSettingsErr of MsgSettingsError


open MessagingClientErrors
open MessagingServiceErrors


module MessagingErrors =
    type MessagingError =
        | MessagingClientErr of MessagingClientError
        | MessagingServiceErr of MessagingServiceError
