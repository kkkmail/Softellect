namespace Softellect.Messaging

open System
open System.Threading

open Softellect.Sys.MessagingPrimitives
open Softellect.Sys.MessagingClientErrors
//open Softellect.Sys.MessagingServiceErrors
open Softellect.Sys.MessagingErrors
open Softellect.Sys.Errors
open Softellect.Sys.TimerEvents

open Softellect.Wcf.Common
open Softellect.Wcf.Client

open Softellect.Messaging.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy
open Softellect.Sys.WcfErrors

module Client =

    /// Maximum number of messages to process in one go.
    let maxNumberOfMessages = 5_000
    let maxMessages = [ for _ in 1..maxNumberOfMessages -> () ]

    let maxNumberOfSmallMessages = 5_000
    let maxNumberOfMediumMessages = 500
    let maxNumberOfLargeMessages = 100

    //let private toError e = e |> MessagingClientErr |> SingleErr |> Error
    //let private addError g f e = ((f |> g |> MessagingClientErr) + e) |> SingleErr |> Error


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


    type MsgResponseHandlerData<'D, 'E> =
        {
            msgAccessInfo : MessagingClientAccessInfo
            toErr : MessagingClientError -> Err<'E>
        }


    type MessagingClientData<'D, 'E> =
        {
            msgAccessInfo : MessagingClientAccessInfo
            //messagingService : IMessagingClient<'D, 'E>
            msgClientProxy : MessagingClientProxy<'D, 'E>
            expirationTime : TimeSpan
        }

        member d.msgResponseHandlerData : MsgResponseHandlerData<'D, 'E> =
            {
                msgAccessInfo = d.msgAccessInfo
                toErr = d.msgClientProxy.toErr
            }

        static member defaultExpirationTime = TimeSpan.FromMinutes 5.0


    type TryReceiveSingleMessageProxy<'D, 'E> =
        {
            saveMessage : Message<'D> -> UnitResult<'E>
            tryDeleteMessage : MessageId -> UnitResult<'E>
            tryPeekMessage : unit -> ResultWithErr<Message<'D> option, 'E>
            tryDeleteFromServer : MessageId -> UnitResult<'E>
            getMessageSize : MessageData<'D> -> MessageSize
            toErr : MessagingClientError -> Err<'E>
        }


    let tryReceiveSingleMessage (proxy : TryReceiveSingleMessageProxy<'D, 'E>) : ResultWithErr<MessageSize option, 'E> =
        //let a e = proxy.toErr (TryReceiveSingleMessageErr e)
        //let addError = addError (proxy.toErr TryReceiveSingleMessageErr)
        let addError (a : TryReceiveSingleMessageError) (e : Err<'E>) : ResultWithErr<MessageSize option, 'E> = failwith "..."

        let result =
            match proxy.tryPeekMessage () with
            | Ok (Some m) ->
                match proxy.saveMessage m with
                | Ok() ->
                    match proxy.tryDeleteFromServer m.messageDataInfo.messageId with
                    | Ok() -> m.messageData |> proxy.getMessageSize |> Some |> Ok
                    | Error e ->
                        match proxy.tryDeleteMessage m.messageDataInfo.messageId with
                        | Ok() -> addError TryDeleteFromServerErr e
                        | Error e1 -> addError TryDeleteFromServerErr (e1 + e)
                | Error e -> addError SaveMessageErr e
            | Ok None -> Ok None
            | Error e -> addError TryPeekMessageErr e

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
            tryPickOutgoingMessage : unit -> ResultWithErr<Message<'D> option, 'E>
            tryDeleteMessage : MessageId -> UnitResult<'E>
            sendMessage : Message<'D> -> UnitResult<'E>
            getMessageSize : MessageData<'D> -> MessageSize
        }


    let trySendSingleMessage (proxy : TrySendSingleMessageProxy<'D, 'E>) =
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
    //type MsgResponseHandler<'D, 'E> private (url) =
    type MsgResponseHandler<'D, 'E> (d : MsgResponseHandlerData<'D, 'E>) =
        let url = d.msgAccessInfo.msgSvcAccessInfo.messagingServiceAccessInfo.httpUrl

        let tryGetWcfService() = tryGetWcfService<IMessagingWcfService> url

        //let getVersionWcfErr e = e |> GetVersionSvcWcfErr |> GetVersionSvcErr |> MessagingServiceErr
        //let msgWcfErr e = e |> MsgWcfErr |> MessageDeliveryErr |> MessagingServiceErr
        //let tryPeekMsgWcfErr e = e |> TryPeekMsgWcfErr |> TryPeekMessageErr |> MessagingServiceErr
        //let tryDeleteMsgWcfErr e = e |> TryDeleteMsgWcfErr |> TryDeleteFromServerErr |> MessagingServiceErr

        let getVersionWcfErr (e : WcfError) : Err<'E> = GeneralMessagingClientErr |> d.toErr
        let msgWcfErr (e : WcfError) : Err<'E> = GeneralMessagingClientErr |> d.toErr
        let tryPeekMsgWcfErr (e : WcfError) : Err<'E> = GeneralMessagingClientErr |> d.toErr
        let tryDeleteMsgWcfErr (e : WcfError) : Err<'E> = GeneralMessagingClientErr |> d.toErr

        let getVersionImpl() = tryCommunicate tryGetWcfService (fun service -> service.getVersion) getVersionWcfErr ()
        let sendMessageImpl m = tryCommunicate tryGetWcfService (fun service -> service.sendMessage) msgWcfErr m
        let tryPeekMessageImpl n = tryCommunicate tryGetWcfService (fun service -> service.tryPeekMessage) tryPeekMsgWcfErr n
        let tryDeleteFromServerImpl x = tryCommunicate tryGetWcfService (fun service -> service.tryDeleteFromServer) tryDeleteMsgWcfErr x

        interface IMessagingClient<'D, 'E> with
            member _.getVersion() = getVersionImpl()
            member _.sendMessage m = sendMessageImpl m
            member _.tryPeekMessage n = tryPeekMessageImpl n
            member _.tryDeleteFromServer x = tryDeleteFromServerImpl x

        //new (i : MessagingClientAccessInfo) = MsgResponseHandler<'D, 'E>(i.msgSvcAccessInfo.messagingServiceAccessInfo.httpUrl)
        //new (i : MessagingServiceAccessInfo) = MsgResponseHandler<'D, 'E>(i.wcfServiceUrl)


    type MessagingClient<'D, 'E>(d : MessagingClientData<'D, 'E>) =
        let proxy = d.msgClientProxy
        let msgClientId = d.msgAccessInfo.msgClientId
        let responseHandler = MsgResponseHandler<'D, 'E>(d.msgResponseHandlerData) :> IMessagingClient<'D, 'E>

        let receiveProxy =
            {
                saveMessage = proxy.saveMessage
                tryDeleteMessage = proxy.tryDeleteMessage
                tryPeekMessage = fun () -> responseHandler.tryPeekMessage msgClientId
                tryDeleteFromServer = fun m -> responseHandler.tryDeleteFromServer (msgClientId, m)
                getMessageSize = d.msgClientProxy.getMessageSize
                toErr = d.msgClientProxy.toErr
            }

        let sendProxy =
            {
                tryPickOutgoingMessage = proxy.tryPickOutgoingMessage
                tryDeleteMessage = proxy.tryDeleteMessage
                sendMessage = responseHandler.sendMessage
                getMessageSize = d.msgClientProxy.getMessageSize
            }

        /// Verifies that we have access to the relevant database and removes all expired messages.
        member m.start() = m.removeExpiredMessages()

        member _.sendMessage (m : MessageInfo<'D>) : UnitResult<'E> =
            createMessage d.msgAccessInfo.msgSvcAccessInfo.messagingDataVersion msgClientId m
            |> proxy.saveMessage

        member _.tryPeekReceivedMessage() : ResultWithErr<Message<'D> option, 'E> = proxy.tryPickIncomingMessage()
        member _.tryRemoveReceivedMessage (m : MessageId) : UnitResult<'E> = proxy.tryDeleteMessage m
        member _.tryReceiveMessages() : UnitResult<'E> = tryReceiveMessages receiveProxy
        member _.trySendMessages() : UnitResult<'E> = trySendMessages sendProxy
        member _.removeExpiredMessages() : UnitResult<'E> = proxy.deleteExpiredMessages d.expirationTime

        member m.messageProcessorProxy : MessageProcessorProxy<'D, 'E> =
            {
                start = m.start
                tryPeekReceivedMessage = m.tryPeekReceivedMessage
                tryRemoveReceivedMessage = m.tryRemoveReceivedMessage
                sendMessage = m.sendMessage
                tryReceiveMessages = m.tryReceiveMessages
                trySendMessages = m.trySendMessages
                removeExpiredMessages = m.removeExpiredMessages
                logger = proxy.logger
                toErr = proxy.toErr
            }

        member _.tryReceiveSingleMessageProxy : TryReceiveSingleMessageProxy<'D, 'E> =
            {
                saveMessage = d.msgClientProxy.saveMessage
                tryDeleteMessage = d.msgClientProxy.tryDeleteMessage
                tryPeekMessage = fun () -> responseHandler.tryPeekMessage d.msgAccessInfo.msgClientId
                tryDeleteFromServer = fun x -> responseHandler.tryDeleteFromServer (d.msgAccessInfo.msgClientId, x)
                getMessageSize = d.msgClientProxy.getMessageSize
                toErr = proxy.toErr
            }

    let mutable private callCount = -1

    let onTryProcessMessage (w : MessageProcessorProxy<'D, 'E>) x f =
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
                    | e -> e |> OnTryProcessMessageExn |> OnTryProcessMessageErr |> w.toErr |> FailedToProcess
                | Ok None -> NothingToDo
                | Error e -> FailedToProcess e
            else BusyProcessing

        Interlocked.Decrement(&callCount) |> ignore
        retVal


    /// Call this function to create timer events necessary for automatic MessagingClient operation.
    /// If you don't call it, then you have to operate MessagingClient by hands.
    let createMessagingClientEventHandlers (w : MessageProcessorProxy<'M, 'E>) =
        let eventHandler _ = w.tryReceiveMessages()
        let i = TimerEventInfo.defaultValue "MessagingClient - tryReceiveMessages" 

        let proxy =
            {
                eventHandler = eventHandler
                logger = w.logger
                toErr = fun e -> e |> TimerEventErr |> w.toErr
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
