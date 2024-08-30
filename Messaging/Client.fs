﻿namespace Softellect.Messaging

open System
open System.Threading

open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Rop
open Softellect.Sys.TimerEvents
open Softellect.Sys.Logging
open Softellect.Wcf.Common
open Softellect.Wcf.Client

open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy

module Client =

    /// Maximum number of messages to process in one go.
    let maxNumberOfMessages = 5_000

    let maxNumberOfSmallMessages = 5_000
    let maxNumberOfMediumMessages = 500
    let maxNumberOfLargeMessages = 100


    let private addMessagingError f e = f + e


    /// Proxy for messaging client event handlers.
    type private MessagingClientEventHandlersProxy =
        {
            trySendMessages : unit -> MessagingUnitResult
            tryReceiveMessages : unit -> MessagingUnitResult
            removeExpiredMessages : unit -> MessagingUnitResult
            getLogger : GetLogger
        }


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


    type MessagingClientData<'D> =
        {
            msgAccessInfo : MessagingClientAccessInfo
            msgClientProxy : MessagingClientProxy<'D>
            logOnError : bool
        }


    type TryReceiveSingleMessageProxy<'D> =
        {
            saveMessage : Message<'D> -> MessagingUnitResult
            tryDeleteMessage : MessageId -> MessagingUnitResult
            tryPeekMessage : unit -> MessagingOptionalResult<'D>
            tryDeleteFromServer : MessageId -> MessagingUnitResult
            getMessageSize : MessageData<'D> -> MessageSize
        }


    let tryReceiveSingleMessage (proxy : TryReceiveSingleMessageProxy<'D>) : Result<MessageSize option, MessagingError> =
        let addError a e = (TryReceiveSingleMessageErr a) + e |> Error

        let result =
            match proxy.tryPeekMessage () with
            | Ok (Some m) ->
                match proxy.saveMessage m with
                | Ok() ->
                    match proxy.tryDeleteFromServer m.messageDataInfo.messageId with
                    | Ok() -> m.messageData |> proxy.getMessageSize |> Some |> Ok
                    | Error e ->
                        match proxy.tryDeleteMessage m.messageDataInfo.messageId with
                        | Ok() -> addError TryReceiveSingleMessageDeleteErr e
                        | Error e1 -> addError TryReceiveSingleMessageDeleteErr (e1 + e)
                | Error e -> addError TryReceiveSingleMessageSaveErr e
            | Ok None -> Ok None
            | Error e -> addError TryReceiveSingleMessagePickErr e

        result


    let private mapper transmitter (c : MessageCount) =
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
//        printfn "tryTransmitMessages: starting..."
        let rec doTryTransmit x c =
            match x with
            | [] -> Ok()
            | _ :: t ->
                match mapper transmitter c with
                | Ok (Some c1) -> doTryTransmit t c1
                | Ok None -> Ok()
                | Error e -> Error e

        let y = doTryTransmit [ for _ in 1..maxNumberOfMessages -> () ] MessageCount.defaultValue
        y


    let tryReceiveMessages proxy = tryTransmitMessages (fun () -> tryReceiveSingleMessage proxy)


    let createMessage messagingDataVersion msgClientId (m : MessageInfo<'D>) =
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


    type TrySendSingleMessageProxy<'D, 'E> =
        {
            tryPickOutgoingMessage : unit -> Result<Message<'D> option, 'E>
            tryDeleteMessage : MessageId -> UnitResult<'E>
            sendMessage : Message<'D> -> UnitResult<'E>
            getMessageSize : MessageData<'D> -> MessageSize
        }


    let trySendSingleMessage (proxy : TrySendSingleMessageProxy<'D, 'E>) =
        //printfn "trySendSingleMessage: starting..."
        match proxy.tryPickOutgoingMessage() with
        | Ok None -> Ok None
        | Ok (Some m) ->
            match proxy.sendMessage m with
            | Ok() ->
                match proxy.tryDeleteMessage m.messageDataInfo.messageId with
                | Ok() -> m.messageData |> proxy.getMessageSize |> Some |> Ok
                | Error e -> Error e
            | Error e -> Error e
        | Error e -> Error e


    let trySendMessages proxy = tryTransmitMessages (fun () -> trySendSingleMessage proxy)


    /// Low level WCF messaging client.
    type MsgResponseHandler<'D> (d : MessagingServiceAccessInfo) =
        let i = d.serviceAccessInfo
        let url = i.getUrl()
        let tryGetWcfService() = tryGetWcfService<IMessagingWcfService> i.communicationType url

        let getVersionWcfErr e = e |> GetVersionWcfErr |> GetVersionErr
        let sendMessageWcfErr e = e |> SendMessageWcfErr |> SendMessageErr
        let tryPickMessageWcfErr e = e |> TryPickMsgWcfErr |> TryPickMessageWcfErr
        let tryDeleteFromServerWcfErr e = e |> TryDeleteFromServerWcfErr |> TryDeleteFromServerErr

        let getVersionImpl() = tryCommunicate tryGetWcfService (fun service -> service.getVersion) getVersionWcfErr ()
        let sendMessageImpl m = tryCommunicate tryGetWcfService (fun service -> service.sendMessage) sendMessageWcfErr m
        let tryPickMessageImpl n = tryCommunicate tryGetWcfService (fun service -> service.tryPickMessage) tryPickMessageWcfErr n
        let tryDeleteFromServerImpl x = tryCommunicate tryGetWcfService (fun service -> service.tryDeleteFromServer) tryDeleteFromServerWcfErr x

        interface IMessagingClient<'D> with
            member _.getVersion() = getVersionImpl()
            member _.sendMessage m = sendMessageImpl m
            member _.tryPickMessage n = tryPickMessageImpl n
            member _.tryDeleteFromServer x = tryDeleteFromServerImpl x


    /// Call this function to create timer events necessary for automatic MessagingClient operation.
    let private createMessagingClientEventHandlers (w : MessagingClientEventHandlersProxy) =
        let logger = w.getLogger (LoggerName $"createMessagingClientEventHandlers")

        logger.logInfo "createMessagingClientEventHandlers - starting..."
        let eventHandler _ = w.tryReceiveMessages()
        let i = TimerEventInfo.defaultValue "MessagingClient - tryReceiveMessages"
        logger.logInfo $"%A{i}"

        let proxy =
            {
                eventHandler = eventHandler
                getLogger = w.getLogger
                toErr = fun e -> e |> TimerEventErr
            }

        let info =
            {
                timerEventInfo = i
                timerProxy = proxy
            }

        let h = TimerEventHandler info
        do h.start()

        let eventHandler1 _ = w.trySendMessages()
        let i1 = { TimerEventInfo.defaultValue "MessagingClient - trySendMessages" with firstDelay = RefreshInterval / 3 |> Some }

        let info1 =
            {
                timerEventInfo = i1
                timerProxy = { proxy with eventHandler = eventHandler1 }
            }

        let h1 = TimerEventHandler info1

        do h1.start()

        let eventHandler2 _ = w.removeExpiredMessages()
        let i2 = { TimerEventInfo.oneHourValue "MessagingClient - removeExpiredMessages" with firstDelay = 2 * RefreshInterval / 3 |> Some }

        let info2 =
            {
                timerEventInfo = i2
                timerProxy = { proxy with eventHandler = eventHandler2 }
            }

        let h2 = TimerEventHandler info2
        do h2.start()


    type MessagingClient<'D>(d : MessagingClientData<'D>) =
        let proxy = d.msgClientProxy
        let msgClientId = d.msgAccessInfo.msgClientId
        let msgSvcAccessInfo = d.msgAccessInfo.msgSvcAccessInfo
        let responseHandler = MsgResponseHandler<'D>(msgSvcAccessInfo) :> IMessagingClient<'D>
        let mutable callCount = -1
        let mutable started = false
        let getLogger = d.msgClientProxy.getLogger

        let incrementCount() = Interlocked.Increment(&callCount)
        let decrementCount() = Interlocked.Decrement(&callCount)

        let receiveProxy =
            {
                saveMessage = proxy.saveMessage
                tryDeleteMessage = proxy.tryDeleteMessage
                tryPeekMessage = fun () -> responseHandler.tryPickMessage msgClientId
                tryDeleteFromServer = fun m -> responseHandler.tryDeleteFromServer (msgClientId, m)
                getMessageSize = d.msgClientProxy.getMessageSize
            }

        let sendProxy =
            {
                tryPickOutgoingMessage = proxy.tryPickOutgoingMessage
                tryDeleteMessage = proxy.tryDeleteMessage
                sendMessage = responseHandler.sendMessage
                getMessageSize = d.msgClientProxy.getMessageSize
            }

        let removeExpiredMessages() = proxy.deleteExpiredMessages msgSvcAccessInfo.expirationTime
        let sendMessage m = createMessage msgSvcAccessInfo.messagingDataVersion msgClientId m |> proxy.saveMessage
        let tryReceiveMessages() = tryReceiveMessages receiveProxy
        let trySendMessages() = trySendMessages sendProxy

        let eventHandlerProxy =
            {
                trySendMessages = trySendMessages
                tryReceiveMessages = tryReceiveMessages
                removeExpiredMessages = removeExpiredMessages
                getLogger = proxy.getLogger
            }

        /// Verifies that we have access to the relevant data storage, starts the timers and removes all expired messages.
        let tryStart() =
            if not started
            then
                match removeExpiredMessages() with
                | Ok() ->
                    started <- true
                    createMessagingClientEventHandlers eventHandlerProxy
                    Ok()
                | Error e -> Error e
            else Ok()

        let onTryProcessMessage (f : Message<'D> -> Result<unit, MessagingError>) =
            let logger = getLogger (LoggerName $"onTryProcessMessage<{typedefof<'D>.Name}>")

            let retVal =
                if incrementCount() = 0
                then
                    match proxy.tryPickIncomingMessage() with
                    | Ok (Some m) ->
                        try
                            let r = f m

                            match proxy.tryDeleteMessage m.messageDataInfo.messageId with
                            | Ok() ->
                                match r with
                                | Ok () -> ProcessedSuccessfully
                                | Error e -> ProcessedWithError e
                            | Error e -> ProcessedWithFailedToRemove e
                        with
                        | e -> e |> OnTryProcessMessageExn |> OnTryProcessMessageErr |> FailedToProcess
                    | Ok None -> NothingToDo
                    | Error e -> FailedToProcess e
                else BusyProcessing

            decrementCount() |> ignore

            match retVal.errorOpt, d.logOnError with
            | Some e, true -> logger.logError $"%A{e}"
            | _ -> ignore()

            retVal

        let onProcessMessages (f : Message<'D> -> MessagingUnitResult) : MessagingUnitResult =
            let elevate e = e |> OnGetMessagesErr
            let addError f e = ((elevate f) + e) |> Error
            let toError e = e |> elevate |> Error

            let rec doFold x r =
                match x with
                | [] -> Ok()
                | _ :: t ->
                    match onTryProcessMessage f with
                    | ProcessedSuccessfully -> doFold t r
                        //match u with
                        //| Ok() -> doFold t r
                        //| Error e -> doFold t ((addError ProcessedSuccessfullyWithInnerErr e, r) ||> combineUnitResults addMessagingError)
                    | ProcessedWithError e -> [ addError ProcessedWithErr e; r ] |> foldUnitResults addMessagingError
                    | ProcessedWithFailedToRemove e -> [ addError ProcessedWithFailedToRemoveErr e; r ] |> foldUnitResults addMessagingError
                    | FailedToProcess e -> addError FailedToProcessErr e
                    | NothingToDo -> Ok()
                    | BusyProcessing -> toError BusyProcessingErr

            let result = doFold [for _ in 1..maxNumberOfMessages -> ()] (Ok())
            result

        //member _.sendMessage (m : MessageInfo<'D>) : MessagingUnitResult =
        //    createMessage msgSvcAccessInfo.messagingDataVersion msgClientId m
        //    |> proxy.saveMessage

        //member _.sendMessage m = sendMessageImpl m

        //member _.tryPickReceivedMessage() : MessagingOptionalResult<'D> = proxy.tryPickIncomingMessage()
        //member _.tryRemoveReceivedMessage (m : MessageId) : MessagingUnitResult = proxy.tryDeleteMessage m
        //member _.tryReceiveMessages() : MessagingUnitResult = tryReceiveMessages receiveProxy
        //member _.trySendMessages() : MessagingUnitResult = trySendMessages sendProxy
        //member _.removeExpiredMessages() : MessagingUnitResult = proxy.deleteExpiredMessages d.msgAccessInfo.msgSvcAccessInfo.expirationTime

        //member _.messageProcessorProxy : MessageProcessorProxy<'D> =
        //    {
        //        tryStart = tryStart
        //        tryPickReceivedMessage = proxy.tryPickIncomingMessage
        //        tryRemoveReceivedMessage = proxy.tryDeleteMessage
        //        sendMessage = sendMessage
        //        //tryReceiveMessages = tryReceiveMessages
        //        //trySendMessages = trySendMessages
        //        //removeExpiredMessages = removeExpiredMessages
        //        getLogger = proxy.getLogger
        //        incrementCount = incrementCount
        //        decrementCount = decrementCount
        //        logOnError = d.logOnError
        //        maxMessages = maxNumberOfMessages
        //    }

        interface IMessageProcessor<'D> with
            member _.tryStart() = tryStart()
            member _.sendMessage d = sendMessage d
            member _.tryProcessMessage f = onTryProcessMessage f
            member _.processMessages f = onProcessMessages f

            //member _.tryPickReceivedMessage() = proxy.tryPickIncomingMessage()
            //member _.tryRemoveReceivedMessage m = proxy.tryDeleteMessage m
            //member _.getLogger = proxy.getLogger
            //member _.incrementCount() = incrementCount()
            //member _.decrementCount() = decrementCount()
            //member _.logOnError = d.logOnError
            //member _.maxMessages = maxNumberOfMessages

        //member _.tryReceiveSingleMessageProxy : TryReceiveSingleMessageProxy<'D> =
        //    {
        //        saveMessage = d.msgClientProxy.saveMessage
        //        tryDeleteMessage = d.msgClientProxy.tryDeleteMessage
        //        tryPeekMessage = fun () -> responseHandler.tryPickMessage d.msgAccessInfo.msgClientId
        //        tryDeleteFromServer = fun x -> responseHandler.tryDeleteFromServer (d.msgAccessInfo.msgClientId, x)
        //        getMessageSize = d.msgClientProxy.getMessageSize
        //    }


    ///// Tries to process a single (first) message (if any) using a given message processor f.
    //let onTryProcessMessage (w : IMessageProcessor<'D>) (f : Message<'D> -> Result<unit, MessagingError>) =
    //    let logger = w.getLogger (LoggerName $"onTryProcessMessage<{typedefof<'D>.Name}>")

    //    let retVal =
    //        if w.incrementCount() = 0
    //        then
    //            match w.tryPickReceivedMessage() with
    //            | Ok (Some m) ->
    //                try
    //                    let r = f m

    //                    match w.tryRemoveReceivedMessage m.messageDataInfo.messageId with
    //                    | Ok() -> ProcessedSuccessfully r
    //                    | Error e -> ProcessedWithFailedToRemove (r, e)
    //                with
    //                | e -> e |> OnTryProcessMessageExn |> OnTryProcessMessageErr |> FailedToProcess
    //            | Ok None -> NothingToDo
    //            | Error e -> FailedToProcess e
    //        else BusyProcessing

    //    w.decrementCount() |> ignore

    //    match retVal.errorOpt, w.logOnError with
    //    | Some e, true -> logger.logError $"%A{e}"
    //    | _ -> ignore()

    //    retVal


    ///// Tries to process all incoming messages but no more than max number of messages (w.maxMessages) in one batch.
    //let onGetMessages<'D> (w : IMessageProcessor<'D>) (f : Message<'D> -> Result<unit, MessagingError>) =
    //    let elevate e = e |> OnGetMessagesErr
    //    let addError f e = ((elevate f) + e) |> Error
    //    let toError e = e |> elevate |> Error

    //    let rec doFold x r =
    //        match x with
    //        | [] -> Ok()
    //        | _ :: t ->
    //            match onTryProcessMessage w f with
    //            | ProcessedSuccessfully u ->
    //                match u with
    //                | Ok() -> doFold t r
    //                | Error e -> doFold t ((addError ProcessedSuccessfullyWithInnerErr e, r) ||> combineUnitResults addMessagingError)
    //            | ProcessedWithError (u, e) -> [ addError ProcessedWithErr e; u; r ] |> foldUnitResults addMessagingError
    //            | ProcessedWithFailedToRemove(u, e) -> [ addError ProcessedWithFailedToRemoveErr e; u; r ] |> foldUnitResults addMessagingError
    //            | FailedToProcess e -> addError FailedToProcessErr e
    //            | NothingToDo -> Ok()
    //            | BusyProcessing -> toError BusyProcessingErr

    //    let result = doFold [for _ in 1..w.maxMessages -> ()] (Ok())
    //    result
