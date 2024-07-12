namespace Softellect.Messaging

open System

open Softellect.Sys.Rop
open Softellect.Sys.Logging
open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Messaging.ServiceInfo

module Proxy =

    let private addMessagingError f e = f + e


    /// Provides IO proxy for messaging client.
    type MessagingClientProxy<'D> =
        {
            tryPickIncomingMessage : unit -> MessagingOptionalResult<'D>
            tryPickOutgoingMessage : unit -> MessagingOptionalResult<'D>
            saveMessage : Message<'D> -> MessagingUnitResult
            tryDeleteMessage : MessageId -> MessagingUnitResult
            deleteExpiredMessages : TimeSpan -> MessagingUnitResult
            getMessageSize : MessageData<'D> -> MessageSize
            logger : MessagingLogger
        }


    /// Provides IO proxy for messaging service.
    type MessagingServiceProxy<'D> =
        {
            tryPickMessage : MessagingClientId -> MessagingOptionalResult<'D>
            saveMessage : Message<'D> -> MessagingUnitResult
            deleteMessage : MessageId -> MessagingUnitResult
            deleteExpiredMessages : TimeSpan -> MessagingUnitResult
            logger : MessagingLogger
        }

        static member defaultValue : MessagingServiceProxy<'D> =
            {
                tryPickMessage = fun _ -> failwith ""
                saveMessage = fun _ -> failwith ""
                deleteMessage = fun _ -> failwith ""
                deleteExpiredMessages = fun _ -> failwith ""
                logger = Logger.defaultValue
            }


    type MessageProcessorResult<'D> =
        | ProcessedSuccessfully of 'D
        | ProcessedWithError of ('D * MessagingError)
        | ProcessedWithFailedToRemove of ('D * MessagingError)
        | FailedToProcess of MessagingError
        | NothingToDo
        | BusyProcessing

        member e.errorOpt =
            match e with
            | ProcessedSuccessfully _ -> None
            | ProcessedWithError (_, e) -> Some e
            | ProcessedWithFailedToRemove (_, e) -> Some e
            | FailedToProcess e -> Some e
            | NothingToDo -> None
            | BusyProcessing -> None


    type MessageProcessorProxy<'D> =
        {
            start : unit -> MessagingUnitResult
            tryPeekReceivedMessage : unit -> MessagingOptionalResult<'D>
            tryRemoveReceivedMessage : MessageId -> MessagingUnitResult
            sendMessage : MessageInfo<'D> -> MessagingUnitResult
            tryReceiveMessages : unit -> MessagingUnitResult
            trySendMessages : unit -> MessagingUnitResult
            removeExpiredMessages : unit -> MessagingUnitResult
            logger : MessagingLogger
            incrementCount : unit -> int
            decrementCount : unit -> int
            logOnError : bool // If true, then message processor will log error if any is encountered. If false, then it is the client responsibility to check for errors.
        }


    type OnProcessMessageType<'S, 'D> = 'S -> Message<'D> -> MessagingStateWithResult<'S>
    type MessageResult<'S> = MessageProcessorResult<'S * MessagingUnitResult>


    type OnGetMessagesProxy<'S, 'D> =
        {
            tryProcessMessage : 'S -> OnProcessMessageType<'S, 'D> -> MessageResult<'S>
            onProcessMessage : 'S -> Message<'D> -> MessagingStateWithResult<'S>
            maxMessages : list<unit>
        }


    let onGetMessages<'S, 'D> (proxy : OnGetMessagesProxy<'S, 'D>) (s : 'S) =
        let elevate e = e |> OnGetMessagesErr
        let addError f e = ((elevate f) + e) |> Error
        let toError e = e |> elevate |> Error

        let rec doFold x (acc, r) =
            match x with
            | [] -> acc, Ok()
            | () :: t ->
                match proxy.tryProcessMessage acc proxy.onProcessMessage with
                | ProcessedSuccessfully (g, u) ->
                    match u with
                    | Ok() -> doFold t (g, r)
                    | Error e -> doFold t (g, (addError ProcessedSuccessfullyWithInnerErr e, r) ||> combineUnitResults addMessagingError)
                | ProcessedWithError ((g, u), e) -> g, [ addError ProcessedWithErr e; u; r ] |> foldUnitResults addMessagingError
                | ProcessedWithFailedToRemove((g, u), e) -> g, [ addError ProcessedWithFailedToRemoveErr e; u; r ] |> foldUnitResults addMessagingError
                | FailedToProcess e -> acc, addError FailedToProcessErr e
                | NothingToDo -> acc, Ok()
                | BusyProcessing -> acc, toError BusyProcessingErr

        let w, result = doFold proxy.maxMessages (s, Ok())
        w, result
