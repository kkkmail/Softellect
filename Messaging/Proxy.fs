namespace Softellect.Messaging

open System

open Softellect.Sys.Primitives
open Softellect.Sys.MessagingPrimitives
open Softellect.Sys.Errors
open Softellect.Sys.MessagingClientErrors
open Softellect.Sys.MessagingServiceErrors

open Softellect.Messaging.Primitives

module Proxy =

    /// Provides IO proxy for messaging client.
    type MessagingClientProxy<'D> =
        {
            tryPickIncomingMessage : unit -> MsgResult<Message<'D> option>
            tryPickOutgoingMessage : unit -> MsgResult<Message<'D> option>
            saveMessage : Message<'D> -> MsgUnitResult
            tryDeleteMessage : MessageId -> MsgUnitResult
            deleteExpiredMessages : TimeSpan -> MsgUnitResult
            getMessageSize : MessageData<'D> -> MessageSize
        }


    /// Provides IO proxy for messaging service.
    type MessagingServiceProxy<'D> =
        {
            tryPickMessage : MessagingClientId -> MsgResult<Message<'D> option>
            saveMessage : Message<'D> -> MsgUnitResult
            deleteMessage : MessageId -> MsgUnitResult
            deleteExpiredMessages : TimeSpan -> MsgUnitResult
        }


    type MessageProcessorResult<'T> =
        | ProcessedSuccessfully of 'T
        | ProcessedWithError of ('T * Err<'E>)
        | ProcessedWithFailedToRemove of ('T * Err<'E>)
        | FailedToProcess of Err<'E>
        | NothingToDo
        | BusyProcessing


    type MessageProcessorProxy<'D> =
        {
            start : unit -> MsgUnitResult
            tryPeekReceivedMessage : unit -> MsgResult<Message<'D> option>
            tryRemoveReceivedMessage : MessageId -> MsgUnitResult
            sendMessage : MessageInfo<'D> -> MsgUnitResult
            tryReceiveMessages : unit -> MsgUnitResult
            trySendMessages : unit -> MsgUnitResult
            removeExpiredMessages : unit -> MsgUnitResult
        }


    type OnProcessMessageType<'S, 'D> = 'S -> Message<'D> -> StateWithResult<'S, 'E>
    type MessageResult<'S> = MessageProcessorResult<'S * UnitResult<'E>>


    type OnGetMessagesProxy<'S, 'D> =
        {
            tryProcessMessage : 'S -> OnProcessMessageType<'S, 'D> -> MessageResult<'S>
            onProcessMessage : 'S -> Message<'D> -> StateWithResult<'S, 'E>
            maxMessages : list<unit>
            onError : OnGetMessagesError -> Err<'E>
        }


    let onGetMessages<'S, 'D> (proxy : OnGetMessagesProxy<'S, 'D>) (s : 'S) =
        let addError f e = ((proxy.onError f) + e) |> Error
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
                        doFold t (g, (addError ProcessedSuccessfullyWithInnerErr e, r) ||> combineUnitResults)
                | ProcessedWithError ((g, u), e) -> g, [ addError ProcessedWithErr e; u; r ] |> foldUnitResults
                | ProcessedWithFailedToRemove((g, u), e) -> g, [ addError ProcessedWithFailedToRemoveErr e; u; r ] |> foldUnitResults
                | FailedToProcess e -> acc, addError FailedToProcessErr e
                | NothingToDo -> acc, Ok()
                | BusyProcessing -> acc, toError BusyProcessingErr

        let w, result = doFold proxy.maxMessages (s, Ok())
        w, result
