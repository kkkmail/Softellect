namespace Softellect.Messaging

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


    type SaveMessageError =
        | SaveMessageDbErr of DbError
        | CannotSaveMessageErr of MessageId


    type OnGetMessagesError =
        | ProcessedWithErr
        | ProcessedWithFailedToRemoveErr
        | FailedToProcessErr
        | BusyProcessingErr


    type GetVersionSvcError =
        | GetVersionSvcWcfErr of WcfError


    type MessageDeliveryError =
        | MsgWcfErr of WcfError


    type TryPickMessageWcfError =
        | TryPickMsgWcfErr of WcfError


    type TryDeleteFromServerError =
        | TryDeleteFromServerWcfErr of WcfError


    type MsgSettingsError =
        | InvalidSettings of string
        | MsgSettingExn of exn
        | FileErr of FileError


    type TryReceiveSingleMessageError =
        | TryReceiveSingleMessagePickErr
        | TryReceiveSingleMessageSaveErr
        | TryReceiveSingleMessageDeleteErr


    type GetVersionError =
        | GetVersionWcfErr of WcfError


    type SendMessageError =
        | SendMessageWcfErr of WcfError


    type OnTryProcessMessageError =
        | OnTryProcessMessageExn of exn


    type MessagingError =
        | MsgAggregateErr of MessagingError * List<MessagingError>
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

        static member private addError a b =
            match a, b with
            | MsgAggregateErr (x, w), MsgAggregateErr (y, z) -> MsgAggregateErr (x, w @ (y :: z))
            | MsgAggregateErr (x, w), _ -> MsgAggregateErr (x, w @ [b])
            | _, MsgAggregateErr (y, z) -> MsgAggregateErr (a, y :: z)
            | _ -> MsgAggregateErr (a, [b])

        static member (+) (a, b) = MessagingError.addError a b
        member a.add b = a + b
