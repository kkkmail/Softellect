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
    type MessagingClientProxy<'M, 'E> =
        {
            tryPickIncomingMessage : unit -> StlResult<Message<'M> option, 'E>
            tryPickOutgoingMessage : unit -> StlResult<Message<'M> option, 'E>
            saveMessage : Message<'M> -> UnitResult<'E>
            tryDeleteMessage : MessageId -> UnitResult<'E>
            deleteExpiredMessages : TimeSpan -> UnitResult<'E>
        }


    /// Provides IO proxy for messaging service.
    type MessagingServiceProxy<'M, 'E> =
        {
            tryPickMessage : MessagingClientId -> StlResult<Message<'M> option, 'E>
            saveMessage : Message<'M> -> UnitResult<'E>
            deleteMessage : MessageId -> UnitResult<'E>
            deleteExpiredMessages : TimeSpan -> UnitResult<'E>
        }


    type MessageProcessorResult<'T, 'E> =
        | ProcessedSuccessfully of 'T
        | ProcessedWithError of ('T * Err<'E>)
        | ProcessedWithFailedToRemove of ('T * Err<'E>)
        | FailedToProcess of Err<'E>
        | NothingToDo
        | BusyProcessing


    type MessageProcessorProxy<'M, 'E> =
        {
            start : unit -> UnitResult<'E>
            tryPeekReceivedMessage : unit -> StlResult<Message<'M> option, 'E>
            tryRemoveReceivedMessage : MessageId -> UnitResult<'E>
            sendMessage : MessageInfo<'M> -> UnitResult<'E>
            tryReceiveMessages : unit -> UnitResult<'E>
            trySendMessages : unit -> UnitResult<'E>
            removeExpiredMessages : unit -> UnitResult<'E>
        }


    type OnProcessMessageType<'S, 'M, 'E> = 'S -> Message<'M> -> StateWithResult<'S, 'E>
    type MessageResult<'S, 'E> = MessageProcessorResult<'S * UnitResult<'E>, 'E>


    type OnGetMessagesProxy<'S, 'M, 'E> =
        {
            tryProcessMessage : 'S -> OnProcessMessageType<'S, 'M, 'E> -> MessageResult<'S, 'E>
            onProcessMessage : 'S -> Message<'M> -> StateWithResult<'S, 'E>
            maxMessages : list<unit>
            onError : OnGetMessagesError -> Err<'E>
        }


    let onGetMessages<'S, 'M, 'E> (proxy : OnGetMessagesProxy<'S, 'M, 'E>) (s : 'S) =
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
