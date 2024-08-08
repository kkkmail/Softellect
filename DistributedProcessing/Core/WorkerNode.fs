namespace Softellect.DistributedProcessing

open System.Threading
open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Rop
open Softellect.Sys.Core
open Softellect.Sys.TimerEvents
open Softellect.Wcf.Common
open Softellect.Wcf.Client
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy
open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.Proxy
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.DataAccess
open Softellect.DistributedProcessing.AppSettings
open Softellect.Sys.Primitives
open Softellect.Sys.Logging
open Softellect.Messaging.Client
open Softellect.Messaging.ServiceProxy

module WorkerNode =

    type private UnitResult = DistributedProcessingUnitResult
    type private Message<'D, 'P> = Message<DistributedProcessingMessageData<'D, 'P>>
    type private MessageInfo<'D, 'P> = MessageInfo<DistributedProcessingMessageData<'D, 'P>>
    type private MessageProcessorProxy<'D, 'P> = MessageProcessorProxy<DistributedProcessingMessageData<'D, 'P>>
    type private DistributedProcessingResult<'T> = Result<'T, DistributedProcessingError>

    type private Logger = Logger<DistributedProcessingError>


    type WorkerNodeMonitorResponse =
        | CannotAccessWrkNode
        | ErrorOccurred of DistributedProcessingError

        override this.ToString() =
            match this with
            | CannotAccessWrkNode -> "Cannot access worker node."
            | ErrorOccurred e -> "Error occurred: " + e.ToString()


    /// The head should contain the latest error and the tail the earliest error.
    let foldUnitResults r = foldUnitResults DistributedProcessingError.addError r


    let onRegister (proxy : OnRegisterProxy<'D, 'P>) s =
        let result =
            {
                partitionerRecipient = proxy.sendMessageProxy.partitionerId
                deliveryType = GuaranteedDelivery
                messageData = proxy.workerNodeInfo |> RegisterWorkerNodePrtMsg
            }.getMessageInfo()
            |> proxy.sendMessageProxy.sendMessage

        s, result


    let onUnregister (proxy : OnRegisterProxy<'D, 'P>) s =
        let result =
            {
                partitionerRecipient = proxy.sendMessageProxy.partitionerId
                deliveryType = GuaranteedDelivery
                messageData = proxy.workerNodeInfo.workerNodeId |> UnregisterWorkerNodePrtMsg
            }.getMessageInfo()
            |> proxy.sendMessageProxy.sendMessage

        s, result


    /// TODO kk:20210511 - At this point having a "state" for a worker node seems totally useless
    /// because now we attempt to restart everything on a [lengthy] timer event. This is to account for NOT
    /// started solvers due to node overload.
    let onStart (proxy : OnStartProxy) s =
        let g() =
            match proxy.loadAllActiveRunQueueId() with
            | Ok m -> m |> List.map proxy.onRunModel |> foldUnitResults
            | Error e -> Error e

        match s.workerNodeState with
        | NotStartedWorkerNode ->
            let w = { s with workerNodeState = StartedWorkerNode }
            w, g()
        | StartedWorkerNode -> s, g()


    let onProcessMessage (proxy : OnProcessMessageProxy<'D>) (m : Message<'D, 'P>) =
        printfn $"onProcessMessage: Starting. messageId: {m.messageDataInfo.messageId}."

        match m.messageData with
        | UserMsg (WorkerNodeMsg x) ->
            match x with
            | RunModelWrkMsg d ->
                printfn $"    onProcessMessage: runQueueId: {d.runQueueId}."

                match proxy.saveWorkerNodeRunModelData d with
                | Ok() ->
                    printfn $"    onProcessMessage: saveWorkerNodeRunModelData with runQueueId: {d.runQueueId} - OK."
                    let result = proxy.onRunModel d.runQueueId
                    printfn $"    onProcessMessage: onRunModel with runQueueId: {d.runQueueId} - %A{result}."
                    result
                | Error e ->
                    printfn $"    onProcessMessage: saveWorkerNodeRunModelData with runQueueId: {d.runQueueId} ERROR: %A{e}."
                    let e1 = OnProcessMessageErr (CannotSaveModelDataErr (m.messageDataInfo.messageId, d.runQueueId))
                    e1 + e |> Error
            | CancelRunWrkMsg q -> q ||> proxy.requestCancellation
            | RequestResultWrkMsg q -> q ||> proxy.notifyOfResults
        | _ -> (m.messageDataInfo.messageId, m.messageData.getInfo()) |> InvalidMessageErr |> OnProcessMessageErr |> Error


    let onGetState (s : WorkerNodeRunnerState) =
        failwith "onGetState is not implemented yet."


    let sendMessageProxy i =
        {
            partitionerId = i.workerNodeServiceInfo.workerNodeInfo.partitionerId
            sendMessage = i.messageProcessorProxy.sendMessage
        }


    let onRegisterProxy i = // : OnRegisterProxy<'D, 'P> =
        {
            workerNodeInfo = i.workerNodeServiceInfo.workerNodeInfo
            sendMessageProxy = sendMessageProxy i
        }


    type WorkerNodeMessage<'S, 'D> =
        | Start of OnStartProxy * AsyncReplyChannel<UnitResult>
        | Register of AsyncReplyChannel<MessagingUnitResult>
        | Unregister of AsyncReplyChannel<MessagingUnitResult>
        | GetMessages of OnGetMessagesProxy<'S, 'D> * AsyncReplyChannel<MessagingUnitResult>
        | GetState of AsyncReplyChannel<WorkerNodeMonitorResponse>


    type WorkerNodeRunner<'D, 'P>(i : WorkerNodeRunnerData<'D, 'P>) =
        let onRegisterProxy = onRegisterProxy i

        let messageLoop =
            MailboxProcessor.Start(fun u ->
                let rec loop s =
                    async
                        {
                            match! u.Receive() with
                            | Start (p, r) -> return! onStart p s |> (withReply r) |> loop
                            | Register r -> return! onRegister onRegisterProxy s |> (withReply r) |> loop
                            | Unregister r -> return! onUnregister onRegisterProxy s |> (withReply r) |> loop
                            | GetMessages (p, r) -> return! onGetMessages p s |> (withReply r) |> loop
                            | GetState r -> return! onGetState s |> (withReply r) |> loop
                        }

                WorkerNodeRunnerState.defaultValue |> loop
                )

        member w.start() = messageLoop.PostAndReply (fun reply -> Start (w.onStartProxy, reply))
        member _.register() =
            match messageLoop.PostAndReply Register with
            | Ok v -> Ok v
            | Error e -> e |> UnableToRegisterWorkerNodeErr |> Error
        member _.unregister() = messageLoop.PostAndReply Unregister
        member w.getMessages() = messageLoop.PostAndReply (fun reply -> GetMessages (w.onGetMessagesProxy, reply))
        member _.getState() = messageLoop.PostAndReply GetState

        member _.onStartProxy =
            {
                loadAllActiveRunQueueId = i.workerNodeProxy.loadAllActiveRunQueueId
                onRunModel = i.workerNodeProxy.onProcessMessageProxy.onRunModel
            }

        member _.onGetMessagesProxy =
            {
                tryProcessMessage = onTryProcessMessage i.messageProcessorProxy
                onProcessMessage = fun w m ->
                    let r = onProcessMessage i.workerNodeProxy.onProcessMessageProxy m

                    let r1 =
                        match r with
                        | Ok v -> Ok v
                        | Error e ->
                            printfn $"onGetMessagesProxy - error: '{e}'."
                            OnGetMessagesErr FailedToProcessErr |> Error

                    w, r1
                maxMessages = WorkerNodeRunnerState.maxMessages
            }


    let createServiceImpl (logger : Logger) (i : WorkerNodeRunnerData<'D, 'P>) =
        let toError = failwith ""
        logger.logInfoString "createServiceImpl: Creating WorkerNodeRunner..."
        let w = WorkerNodeRunner i

        match i.workerNodeServiceInfo.workerNodeInfo.isInactive with
        | false ->
            logger.logInfoString "createServiceImpl: Registering..."
            match w.register >-> w.start |> evaluate with
            | Ok() ->
                let getMessages() =
                    match w.getMessages() with
                    | Ok () -> Ok ()
                    | Error e -> CreateServiceImplWorkerNodeErr e |> Error

                let i1 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue logger toError getMessages "WorkerNodeRunner - getMessages"
//                let h = TimerEventHandler(TimerEventHandlerInfo.defaultValue logger getMessages "WorkerNodeRunner - getMessages")
                let h = TimerEventHandler i1
                do h.start()

                // Attempt to restart solvers in case they did not start (due to whatever reason) or got killed.
                let i2 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue logger toError getMessages "WorkerNodeRunner - start"
                //let s = ClmEventHandler(ClmEventHandlerInfo.oneHourValue logger w.start "WorkerNodeRunner - start")
                let s = TimerEventHandler i2
                do s.start()

                Ok w
            | Error e -> Error e
        | true ->
            logger.logInfoString "createServiceImpl: Unregistering..."
            match w.unregister() with
            | Ok() -> failwith "createServiceImpl for inactive worker node is not implemented yet."
            | Error e -> CreateServiceImplWorkerNodeErr e |> Error


    type WorkerNodeRunner<'D, 'P>
        with
        static member create messagingDataVersion tryRunSolverProcess (j : DistributedProcessingResult<WorkerNodeServiceInfo>) =
            let logger = Logger.defaultValue
            let addError f e = (f + e) |> Error
            let c = getWorkerNodeSvcConnectionString

            let sr n (q : RunQueueId) : UnitResult =
                match tryRunSolverProcess n q with
                | Some _ -> Ok()
                | None -> q |> CannotRunModelErr |> OnRunModelErr |> Error

            match j with
            | Ok i ->
                let w =
                    let messagingClientAccessInfo = i.messagingClientAccessInfo
                    let getMessageSize (m : DistributedProcessingMessageData<'D, 'P>) = m.getMessageSize()

                    let j =
                        {
                            messagingClientName = WorkerNodeServiceName.netTcpServiceName.value.value |> MessagingClientName
                            storageType = c |> MsSqlDatabase
                            messagingDataVersion = messagingDataVersion
                        }

                    let messagingClientData =
                        {
                            msgAccessInfo = messagingClientAccessInfo
                            communicationType = NetTcpCommunication
                            msgClientProxy = createMessagingClientProxy getMessageSize j messagingClientAccessInfo.msgClientId
                            expirationTime = MessagingClientData<'D>.defaultExpirationTime
                            logOnError = true
                        }

                    let messagingClient = MessagingClient messagingClientData

                    match messagingClient.start() with
                    | Ok() ->
                        let n =
                            {
                                workerNodeServiceInfo = i
                                workerNodeProxy = WorkerNodeProxy<'D>.create c (sr i.workerNodeInfo.noOfCores)
                                messageProcessorProxy = messagingClient.messageProcessorProxy
                            }
                            |> createServiceImpl logger

                        match n with
                        | Ok v -> Ok v
                        | Error e -> addError UnableToCreateWorkerNodeServiceErr e
                    | Error e -> UnableToStartMessagingClientErr e |> Error

                w
            | Error e -> Error e
