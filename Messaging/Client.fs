namespace Softellect.Messaging

open System

open Softellect.Sys.MessagingPrimitives
open Softellect.Sys.MessagingClientErrors
open Softellect.Sys.Errors
open Softellect.Sys.TimerEvents

open Softellect.Messaging.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy
open System.Threading

module Client =

    //type MessagingClient<'M, 'E>(d: unit) =
    //    member _.x = 1



    /// Maximum number of messages to process in one go.
    let maxNumberOfMessages = 5_000

    let maxNumberOfSmallMessages = 5_000
    let maxNumberOfMediumMessages = 500
    let maxNumberOfLargeMessages = 100

    let private toError e = e |> MessagingClientErr |> Error
    let private addError g f e = ((f |> g |> MessagingClientErr) + e) |> Error

    type MessageCount =
        {
            smallMessages : int
            mediumMessages : int
            largeMessages : int
        }

        static member defaultValue =
            {
                smallMessages = 0
                mediumMessages = 0
                largeMessages = 0
            }

        static member maxAllowed =
            {
                smallMessages = maxNumberOfSmallMessages
                mediumMessages = maxNumberOfMediumMessages
                largeMessages = maxNumberOfMediumMessages
            }

        member t.canProcess =
            let m = MessageCount.maxAllowed

            if t.smallMessages < m.smallMessages && t.mediumMessages < m.mediumMessages && t.largeMessages < m.largeMessages then true
            else false

        member t.onSmallMessage() = { t with smallMessages  = t.smallMessages + 1 }
        member t.onMediumMessage() = { t with mediumMessages = t.mediumMessages + 1 }
        member t.onLargeMessage() = { t with largeMessages = t.largeMessages + 1 }


    type MessagingClientData<'M, 'E> =
        {
            msgAccessInfo : MessagingClientAccessInfo
            messagingService : IMessagingService<'M, 'E>
            msgClientProxy : MessagingClientProxy<'M, 'E>
            expirationTime : TimeSpan
        }

        static member defaultExpirationTime = TimeSpan.FromMinutes 5.0

        static member maxMessages = [ for _ in 1..maxNumberOfMessages -> () ]


    type TryReceiveSingleMessageProxy<'M, 'E> =
        {
            saveMessage : Message<'M> -> UnitResult<'E>
            tryDeleteMessage : MessageId -> UnitResult<'E>
            tryPeekMessage : unit -> StlResult<Message<'M> option, 'E>
            tryDeleteFromServer : MessageId -> UnitResult<'E>
        }


    let tryReceiveSingleMessage (proxy : TryReceiveSingleMessageProxy<'M, 'E>) =
        let addError = addError TryReceiveSingleMessageErr

        let result =
            match proxy.tryPeekMessage () with
            | Ok (Some m) ->
                match proxy.saveMessage m with
                | Ok() ->
                    match proxy.tryDeleteFromServer m.messageDataInfo.messageId with
                    | Ok() -> m.messageData.getMessageSize() |> Some |> Ok
                    | Error e ->
                        match proxy.tryDeleteMessage m.messageDataInfo.messageId with
                        | Ok() -> addError TryDeleteFromServerErr e
                        | Error e1 -> addError TryDeleteFromServerErr (e1 + e)
                | Error e -> addError SaveMessageErr e
            | Ok None -> Ok None
            | Error e -> addError TryPeekMessageErr e

        result


    let mapper transmitter (c : MessageCount) =
        match c.canProcess with
        | true ->
            match transmitter() with
            | Ok None -> None |> Ok
            | Ok (Some SmallSize) -> c.onSmallMessage() |> Some |> Ok
            | Ok (Some MediumSize) -> c.onMediumMessage() |> Some |> Ok
            | Ok (Some LargeSize) -> c.onLargeMessage() |> Some |> Ok
            | Error e -> Error e
        | false -> None |> Ok


    let tryTransmitMessages transmitter =
        let rec doTryTransmit x c =
            match x with
            | [] -> Ok()
            | _ :: t ->
                match mapper transmitter c with
                | Ok (Some c1) -> doTryTransmit t c1
                | Ok None -> Ok()
                | Error e -> Error e

        let y = doTryTransmit MessagingClientData.maxMessages MessageCount.defaultValue
        y


    let tryReceiveMessages proxy = tryTransmitMessages (fun () -> tryReceiveSingleMessage proxy)


    let createMessage messagingDataVersion msgClientId (m : MessageInfo<'M>) =
        {
            messageDataInfo =
                {
                    messageId = MessageId.create()
                    dataVersion = messagingDataVersion
                    sender = msgClientId
                    recipientInfo = m.recipientInfo
                    createdOn = DateTime.Now
                }

            messageData = m.messageData
        }


    let onSendMessage saveMessage msgClientId m = createMessage msgClientId m |> saveMessage


    type TrySendSingleMessageProxy<'M, 'E> =
        {
            tryPickOutgoingMessage : unit -> StlResult<Message<'M> option, 'E>
            tryDeleteMessage : MessageId -> UnitResult<'E>
            sendMessage : Message<'M> -> UnitResult<'E>
        }


    let trySendSingleMessage (proxy : TrySendSingleMessageProxy<'M, 'E>) =
        match proxy.tryPickOutgoingMessage() with
        | Ok None -> Ok None
        | Ok (Some m) ->
            match proxy.sendMessage m with
            | Ok() ->
                match proxy.tryDeleteMessage m.messageDataInfo.messageId with
                | Ok() -> m.messageData.getMessageSize() |> Some |> Ok
                | Error e -> Error e
            | Error e -> Error e
        | Error e -> Error e


    let trySendMessages proxy = tryTransmitMessages (fun () -> trySendSingleMessage proxy)


    type MessagingClient<'M, 'E>(d : MessagingClientData<'M, 'E>) =
        let proxy = d.msgClientProxy
        let msgClientId = d.msgAccessInfo.msgClientId

        let receiveProxy =
            {
                saveMessage = proxy.saveMessage
                tryDeleteMessage = proxy.tryDeleteMessage
                tryPeekMessage = fun () -> d.messagingService.tryPeekMessage msgClientId
                tryDeleteFromServer = fun m -> d.messagingService.tryDeleteFromServer (msgClientId, m)
            }

        let sendProxy =
            {
                tryPickOutgoingMessage = proxy.tryPickOutgoingMessage
                tryDeleteMessage = proxy.tryDeleteMessage
                sendMessage = d.messagingService.sendMessage
            }

        /// Verifies that we have access to the relevant database and removes all expired messages.
        member m.start() = m.removeExpiredMessages()

        member _.sendMessage (m : MessageInfo<'M>) : UnitResult<'E> =
            createMessage d.msgAccessInfo.msgSvcAccessInfo.messagingDataVersion msgClientId m
            |> proxy.saveMessage

        member _.tryPeekReceivedMessage() : StlResult<Message<'M> option, 'E> = proxy.tryPickIncomingMessage()
        member _.tryRemoveReceivedMessage (m : MessageId) : UnitResult<'E> = proxy.tryDeleteMessage m
        member _.tryReceiveMessages() : UnitResult<'E> = tryReceiveMessages receiveProxy
        member _.trySendMessages() : UnitResult<'E> = trySendMessages sendProxy
        member m.removeExpiredMessages() : UnitResult<'E> = proxy.deleteExpiredMessages d.expirationTime


        member m.messageProcessorProxy : MessageProcessorProxy<'M, 'E> =
            {
                start = m.start
                tryPeekReceivedMessage = m.tryPeekReceivedMessage
                tryRemoveReceivedMessage = m.tryRemoveReceivedMessage
                sendMessage = m.sendMessage
                tryReceiveMessages = m.tryReceiveMessages
                trySendMessages = m.trySendMessages
                removeExpiredMessages = m.removeExpiredMessages
            }


        member _.tryReceiveSingleMessageProxy : TryReceiveSingleMessageProxy<'M, 'E> =
            {
                saveMessage = d.msgClientProxy.saveMessage
                tryDeleteMessage = d.msgClientProxy.tryDeleteMessage
                tryPeekMessage = fun () -> d.messagingService.tryPeekMessage d.msgAccessInfo.msgClientId
                tryDeleteFromServer = fun x -> d.messagingService.tryDeleteFromServer (d.msgAccessInfo.msgClientId, x)
            }


    let mutable private callCount = -1


    let onTryProcessMessage (w : MessageProcessorProxy<'M, 'E>) x f =
        let retVal =
            if Interlocked.Increment(&callCount) = 0
            then
                match w.tryPeekReceivedMessage() with
                | Ok (Some m) ->
                    try
                        let r = f x m

                        match w.tryRemoveReceivedMessage m.messageDataInfo.messageId with
                        | Ok() -> ProcessedSuccessfully r
                        | Error e -> ProcessedWithFailedToRemove (r, e)
                    with
                    | e -> e |> OnTryProcessMessageExn |> OnTryProcessMessageErr |> MessagingClientErr |> FailedToProcess
                | Ok None -> NothingToDo
                | Error e -> FailedToProcess e
            else BusyProcessing

        Interlocked.Decrement(&callCount) |> ignore
        retVal


    /// Call this function to create timer events necessary for automatic MessagingClient operation.
    /// If you don't call it, then you have to operate MessagingClient by hands.
    let createMessagingClientEventHandlers logger (w : MessageProcessorProxy<'M, 'E>) =
        let eventHandler _ = w.tryReceiveMessages()
        let h = TimerEventInfo<'E>.defaultValue logger eventHandler "MessagingClient - tryReceiveMessages" |> TimerEventHandler
        do h.start()

        let eventHandler1 _ = w.trySendMessages()
        let h1 =
             { TimerEventInfo<'E>.defaultValue logger eventHandler1 "MessagingClient - trySendMessages" with firstDelay = RefreshInterval / 3 |> Some }
             |> TimerEventHandler

        do h1.start()

        let eventHandler2 _ = w.removeExpiredMessages()
        let h2 =
            { TimerEventInfo<'E>.oneHourValue logger eventHandler2 "MessagingClient - removeExpiredMessages" with firstDelay = 2 * RefreshInterval / 3 |> Some }
            |> TimerEventHandler

        do h2.start()


