namespace Softellect.Messaging

open System
open System.Threading

open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Rop
open Softellect.Sys.Primitives
open Softellect.Sys.TimerEvents

open Softellect.Wcf.Common
open Softellect.Wcf.Client

open Softellect.Messaging.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy

module Client =

    /// Maximum number of messages to process in one go.
    let maxNumberOfMessages = 5_000
    let maxMessages = [ for _ in 1..maxNumberOfMessages -> () ]

    let maxNumberOfSmallMessages = 5_000
    let maxNumberOfMediumMessages = 500
    let maxNumberOfLargeMessages = 100


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


    type MsgResponseHandlerData<'D> =
        {
            msgAccessInfo : MessagingClientAccessInfo
            communicationType : WcfCommunicationType
        }


    type MessagingClientData<'D> =
        {
            msgAccessInfo : MessagingClientAccessInfo
            communicationType : WcfCommunicationType
            msgClientProxy : MessagingClientProxy<'D>
            expirationTime : TimeSpan
            logOnError : bool
        }

        member d.msgResponseHandlerData : MsgResponseHandlerData<'D> =
            {
                msgAccessInfo = d.msgAccessInfo
                communicationType = d.communicationType
            }

        static member defaultExpirationTime = TimeSpan.FromMinutes 5.0

        static member create (proxy : MessagingClientProxy<'D>) expiration communicationType info =
            {
                msgAccessInfo = info
                communicationType = communicationType
                msgClientProxy = proxy
                expirationTime = expiration
                logOnError = true
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

        let y = doTryTransmit maxMessages MessageCount.defaultValue
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
    type MsgResponseHandler<'D, 'E> (d : MsgResponseHandlerData<'D>) =
        let i = d.msgAccessInfo.msgSvcAccessInfo.messagingServiceAccessInfo
        let n = i.netTcpServiceInfo
        let url = i.getUrl d.communicationType
        let tryGetWcfService() = tryGetWcfService<IMessagingWcfService> d.communicationType n.netTcpSecurityMode url

        let getVersionWcfErr e = e |> GetVersionWcfErr |> GetVersionErr
        let sendMessageWcfErr e = e |> SendMessageWcfErr |> SendMessageErr
        let tryPickMessageWcfErr e = e |> TryPickMsgWcfErr |> TryPickMessageWcfErr
        let tryDeleteFromServerWcfErr e = e |> TryDeleteFromServerWcfErr |> TryDeleteFromServerErr

        let getVersionImpl() = tryCommunicate tryGetWcfService (fun service -> service.getVersion) getVersionWcfErr ()
        let sendMessageImpl m = tryCommunicate tryGetWcfService (fun service -> service.sendMessage) sendMessageWcfErr m
        let tryPickMessageImpl n = tryCommunicate tryGetWcfService (fun service -> service.tryPeekMessage) tryPickMessageWcfErr n
        let tryDeleteFromServerImpl x = tryCommunicate tryGetWcfService (fun service -> service.tryDeleteFromServer) tryDeleteFromServerWcfErr x

        interface IMessagingClient<'D> with
            member _.getVersion() = getVersionImpl()
            member _.sendMessage m = sendMessageImpl m
            member _.tryPickMessage n = tryPickMessageImpl n
            member _.tryDeleteFromServer x = tryDeleteFromServerImpl x


    /// Call this function to create timer events necessary for automatic MessagingClient operation.
    /// If you don't call it, then you have to operate MessagingClient by hands.
    let private createMessagingClientEventHandlers (w : MessageProcessorProxy<'D>) =
        w.logger.logInfoString "createMessagingClientEventHandlers - starting..."
        let eventHandler _ = w.tryReceiveMessages()
        let i = TimerEventInfo.defaultValue "MessagingClient - tryReceiveMessages"
        w.logger.logInfoString $"%A{i}"

        let proxy =
            {
                eventHandler = eventHandler
                logger = w.logger
                toErr = fun e -> e |> TimerEventErr
            }

        let h = TimerEventHandler (i, proxy)
        do h.start()

        let eventHandler1 _ = w.trySendMessages()
        let i1 = { TimerEventInfo.defaultValue "MessagingClient - trySendMessages" with firstDelay = RefreshInterval / 3 |> Some }
        let h1 = TimerEventHandler (i1, { proxy with eventHandler = eventHandler1 })

        do h1.start()

        let eventHandler2 _ = w.removeExpiredMessages()
        let i2 = { TimerEventInfo.oneHourValue "MessagingClient - removeExpiredMessages" with firstDelay = 2 * RefreshInterval / 3 |> Some }
        let h2 = TimerEventHandler (i2, { proxy with eventHandler = eventHandler2 })
        do h2.start()


    type MessagingClient<'D>(d : MessagingClientData<'D>) =
        let proxy = d.msgClientProxy
        let msgClientId = d.msgAccessInfo.msgClientId
        let responseHandler = MsgResponseHandler<'D, 'E>(d.msgResponseHandlerData) :> IMessagingClient<'D>
        let mutable callCount = -1
        let mutable started = false

        let incrementCount() = Interlocked.Increment(&callCount)
        let decrementCount() = Interlocked.Decrement(&callCount)

        let receiveProxy =
            {
                saveMessage = proxy.saveMessage
                tryDeleteMessage = proxy.tryDeleteMessage
                tryPeekMessage = fun () -> responseHandler.tryPickMessage msgClientId
                tryDeleteFromServer = fun m -> responseHandler.tryDeleteFromServer (msgClientId, m)
                getMessageSize = d.msgClientProxy.getMessageSize
                //toErr = d.msgClientProxy.toErr
                //addError = proxy.addError
            }

        let sendProxy =
            {
                tryPickOutgoingMessage = proxy.tryPickOutgoingMessage
                tryDeleteMessage = proxy.tryDeleteMessage
                sendMessage = responseHandler.sendMessage
                getMessageSize = d.msgClientProxy.getMessageSize
            }

        /// Verifies that we have access to the relevant data storage, starts the timers and removes all expired messages.
        member m.start() =
            if not started
            then
                match m.removeExpiredMessages() with
                | Ok() ->
                    started <- true
                    createMessagingClientEventHandlers m.messageProcessorProxy
                    Ok()
                | Error e -> Error e
            else Ok()

        member _.sendMessage (m : MessageInfo<'D>) : MessagingUnitResult =
            createMessage d.msgAccessInfo.msgSvcAccessInfo.messagingDataVersion msgClientId m
            |> proxy.saveMessage

        member _.tryPickReceivedMessage() : MessagingOptionalResult<'D> = proxy.tryPickIncomingMessage()
        member _.tryRemoveReceivedMessage (m : MessageId) : MessagingUnitResult = proxy.tryDeleteMessage m
        member _.tryReceiveMessages() : MessagingUnitResult = tryReceiveMessages receiveProxy
        member _.trySendMessages() : MessagingUnitResult = trySendMessages sendProxy
        member _.removeExpiredMessages() : MessagingUnitResult = proxy.deleteExpiredMessages d.expirationTime

        member m.messageProcessorProxy : MessageProcessorProxy<'D> =
            {
                start = m.start
                tryPeekReceivedMessage = m.tryPickReceivedMessage
                tryRemoveReceivedMessage = m.tryRemoveReceivedMessage
                sendMessage = m.sendMessage
                tryReceiveMessages = m.tryReceiveMessages
                trySendMessages = m.trySendMessages
                removeExpiredMessages = m.removeExpiredMessages
                logger = proxy.logger
                incrementCount = incrementCount
                decrementCount = decrementCount
                logOnError = d.logOnError
            }

        member _.tryReceiveSingleMessageProxy : TryReceiveSingleMessageProxy<'D> =
            {
                saveMessage = d.msgClientProxy.saveMessage
                tryDeleteMessage = d.msgClientProxy.tryDeleteMessage
                tryPeekMessage = fun () -> responseHandler.tryPickMessage d.msgAccessInfo.msgClientId
                tryDeleteFromServer = fun x -> responseHandler.tryDeleteFromServer (d.msgAccessInfo.msgClientId, x)
                getMessageSize = d.msgClientProxy.getMessageSize
                //toErr = proxy.toErr
                //addError = proxy.addError
            }


    let onTryProcessMessage (w : MessageProcessorProxy<'D>) x f =
        let retVal =
            if w.incrementCount() = 0
            then
                match w.tryPeekReceivedMessage() with
                | Ok (Some m) ->
                    try
                        let r = f x m

                        match w.tryRemoveReceivedMessage m.messageDataInfo.messageId with
                        | Ok() -> ProcessedSuccessfully r
                        | Error e -> ProcessedWithFailedToRemove (r, e)
                    with
                    | e -> e |> OnTryProcessMessageExn |> OnTryProcessMessageErr |> FailedToProcess
                | Ok None -> NothingToDo
                | Error e -> FailedToProcess e
            else BusyProcessing

        w.decrementCount() |> ignore

        match retVal.errorOpt, w.logOnError with
        | Some e, true -> w.logger.logErrorData e
        | _ -> ignore()

        retVal
