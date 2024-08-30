namespace Softellect.Messaging

open System

open Softellect.Sys.Rop
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

        //static member defaultValue : MessagingServiceProxy<'D> =
        //    {
        //        tryPickMessage = fun _ -> failwith ""
        //        saveMessage = fun _ -> failwith ""
        //        deleteMessage = fun _ -> failwith ""
        //        deleteExpiredMessages = fun _ -> failwith ""
        //        getLogger = Logger.defaultValue
        //    }


    //type MessageProcessorResult<'D> =
    //    | ProcessedSuccessfully of 'D
    //    | ProcessedWithError of ('D * MessagingError)
    //    | ProcessedWithFailedToRemove of ('D * MessagingError)
    //    | FailedToProcess of MessagingError
    //    | NothingToDo
    //    | BusyProcessing

    //    member e.errorOpt =
    //        match e with
    //        | ProcessedSuccessfully _ -> None
    //        | ProcessedWithError (_, e) -> Some e
    //        | ProcessedWithFailedToRemove (_, e) -> Some e
    //        | FailedToProcess e -> Some e
    //        | NothingToDo -> None
    //        | BusyProcessing -> None


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
    //type MessageProcessorProxy<'D> =
    //    {
    //        tryStart : unit -> MessagingUnitResult
    //        tryPickReceivedMessage : unit -> MessagingOptionalResult<'D>
    //        tryRemoveReceivedMessage : MessageId -> MessagingUnitResult
    //        sendMessage : MessageInfo<'D> -> MessagingUnitResult
    //        //tryReceiveMessages : unit -> MessagingUnitResult
    //        //trySendMessages : unit -> MessagingUnitResult
    //        //removeExpiredMessages : unit -> MessagingUnitResult
    //        getLogger : GetLogger
    //        incrementCount : unit -> int
    //        decrementCount : unit -> int
    //        logOnError : bool // If true, then message processor will log error if any is encountered. If false, then it is the client responsibility to check for errors.
    //        maxMessages : int // Max number of message to process in one batch.
    //    }
    type IMessageProcessor<'D> =
        abstract tryStart : unit -> MessagingUnitResult
        abstract sendMessage : MessageInfo<'D> -> MessagingUnitResult
        abstract tryProcessMessage : (Message<'D> -> MessagingUnitResult) -> MessageProcessorResult
        abstract processMessages : (Message<'D> -> MessagingUnitResult) -> MessagingUnitResult

        //abstract tryPickReceivedMessage : unit -> MessagingOptionalResult<'D>
        //abstract tryRemoveReceivedMessage : MessageId -> MessagingUnitResult
        //abstract getLogger : GetLogger
        //abstract incrementCount : unit -> int
        //abstract decrementCount : unit -> int
        //abstract logOnError : bool // If true, then message processor will log error if any is encountered. If false, then it is the client responsibility to check for errors.
        //abstract maxMessages : int // Max number of message to process in one batch.
