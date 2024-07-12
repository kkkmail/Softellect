namespace Softellect.Messaging

open System

open Softellect.Sys.Rop
open Softellect.Sys.Logging
open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors

module Proxy =

    /// Provides IO proxy for messaging client.
    type MessagingClientProxy<'D, 'E> =
        {
            tryPickIncomingMessage : unit -> Result<Message<'D> option, 'E>
            tryPickOutgoingMessage : unit -> Result<Message<'D> option, 'E>
            saveMessage : Message<'D> -> UnitResult<'E>
            tryDeleteMessage : MessageId -> UnitResult<'E>
            deleteExpiredMessages : TimeSpan -> UnitResult<'E>
            getMessageSize : MessageData<'D> -> MessageSize
            logger : Logger<'E>
            toErr : MessagingClientError -> 'E
            addError : 'E -> 'E -> 'E
        }


    /// Provides IO proxy for messaging service.
    type MessagingServiceProxy<'D, 'E> =
        {
            tryPickMessage : MessagingClientId -> Result<Message<'D> option, 'E>
            saveMessage : Message<'D> -> UnitResult<'E>
            deleteMessage : MessageId -> UnitResult<'E>
            deleteExpiredMessages : TimeSpan -> UnitResult<'E>
            logger : Logger<'E>
            toErr : MessagingServiceError -> 'E
        }

        static member defaultValue : MessagingServiceProxy<'D, 'E> =
            {
                tryPickMessage = fun _ -> failwith ""
                saveMessage = fun _ -> failwith ""
                deleteMessage = fun _ -> failwith ""
                deleteExpiredMessages = fun _ -> failwith ""
                logger = Logger.defaultValue
                toErr = fun _ -> failwith ""
            }


    type MessageProcessorResult<'T, 'E> =
        | ProcessedSuccessfully of 'T
        | ProcessedWithError of ('T * 'E)
        | ProcessedWithFailedToRemove of ('T * 'E)
        | FailedToProcess of 'E
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


    type MessageProcessorProxy<'D, 'E> =
        {
            start : unit -> UnitResult<'E>
            tryPeekReceivedMessage : unit -> Result<Message<'D> option, 'E>
            tryRemoveReceivedMessage : MessageId -> UnitResult<'E>
            sendMessage : MessageInfo<'D> -> UnitResult<'E>
            tryReceiveMessages : unit -> UnitResult<'E>
            trySendMessages : unit -> UnitResult<'E>
            removeExpiredMessages : unit -> UnitResult<'E>
            logger : Logger<'E>
            toErr : MessagingClientError -> 'E
            incrementCount : unit -> int
            decrementCount : unit -> int
            logOnError : bool // If true, then message processor will log error if any is encountered. If false, then it is the client responsibility to check for errors.
        }


    type OnProcessMessageType<'S, 'D, 'E> = 'S -> Message<'D> -> StateWithResult<'S, 'E>
    type MessageResult<'S, 'E> = MessageProcessorResult<'S * UnitResult<'E>, 'E>


    type OnGetMessagesProxy<'S, 'D, 'E> =
        {
            tryProcessMessage : 'S -> OnProcessMessageType<'S, 'D, 'E> -> MessageResult<'S, 'E>
            onProcessMessage : 'S -> Message<'D> -> StateWithResult<'S, 'E>
            maxMessages : list<unit>
            onError : OnGetMessagesError -> 'E
            addError : 'E -> 'E -> 'E
        }


    let onGetMessages<'S, 'D, 'E> (proxy : OnGetMessagesProxy<'S, 'D, 'E>) (s : 'S) =
        let addError f e = (proxy.addError (proxy.onError f) e) |> Error
        let toError e = e |> proxy.onError |> Error

        let rec doFold x (acc, r) =
            match x with
            | [] -> acc, Ok()
            | () :: t ->
                match proxy.tryProcessMessage acc proxy.onProcessMessage with
                | ProcessedSuccessfully (g, u) ->
                    match u with
                    | Ok() -> doFold t (g, r)
                    | Error e ->
                        doFold t (g, (addError ProcessedSuccessfullyWithInnerErr e, r) ||> combineUnitResults proxy.addError)
                | ProcessedWithError ((g, u), e) -> g, [ addError ProcessedWithErr e; u; r ] |> foldUnitResults proxy.addError
                | ProcessedWithFailedToRemove((g, u), e) -> g, [ addError ProcessedWithFailedToRemoveErr e; u; r ] |> foldUnitResults proxy.addError
                | FailedToProcess e -> acc, addError FailedToProcessErr e
                | NothingToDo -> acc, Ok()
                | BusyProcessing -> acc, toError BusyProcessingErr

        let w, result = doFold proxy.maxMessages (s, Ok())
        w, result
