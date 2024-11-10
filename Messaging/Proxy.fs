namespace Softellect.Messaging

open System

open Softellect.Sys.Logging
open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Messaging.ServiceInfo

module Proxy =

    /// Provides IO proxy for messaging client.
    /// 'D is the strongly typed data that is being sent / received by messaging service.
    type MessagingClientProxy<'D> =
        {
            tryPickIncomingMessage : unit -> MessagingOptionalResult<'D>
            tryPickOutgoingMessage : unit -> MessagingOptionalResult<'D>
            saveMessage : Message<'D> -> MessagingUnitResult
            tryDeleteMessage : MessageId -> MessagingUnitResult
            deleteExpiredMessages : TimeSpan -> MessagingUnitResult
            getMessageSize : MessageData<'D> -> MessageSize
            getLogger : GetLogger
        }


    /// Provides IO proxy for messaging service.
    type MessagingServiceProxy<'D> =
        {
            tryPickMessage : MessagingClientId -> MessagingOptionalResult<'D>
            saveMessage : Message<'D> -> MessagingUnitResult
            deleteMessage : MessageId -> MessagingUnitResult
            deleteExpiredMessages : TimeSpan -> MessagingUnitResult
            getLogger : GetLogger
        }


    type MessageProcessorResult =
        | ProcessedSuccessfully
        | ProcessedWithError of MessagingError
        | ProcessedWithFailedToRemove of MessagingError
        | FailedToProcess of MessagingError
        | NothingToDo
        | BusyProcessing

        member e.errorOpt =
            match e with
            | ProcessedSuccessfully -> None
            | ProcessedWithError e -> Some e
            | ProcessedWithFailedToRemove e -> Some e
            | FailedToProcess e -> Some e
            | NothingToDo -> None
            | BusyProcessing -> None


    /// High level message processor with all the parameters baked in.
    type IMessageProcessor<'D> =
        abstract tryStart : unit -> MessagingUnitResult
        abstract tryStop : unit -> MessagingUnitResult
        abstract sendMessage : MessageInfo<'D> -> MessagingUnitResult
        abstract tryProcessMessage : (Message<'D> -> MessagingUnitResult) -> MessageProcessorResult
        abstract processMessages : (Message<'D> -> MessagingUnitResult) -> MessagingUnitResult
