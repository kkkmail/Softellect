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
    let x = 1

    ////type private UnitResult = DistributedProcessingUnitResult
    //type private DistributedProcessingMessage<'D, 'P> = Message<DistributedProcessingMessageData<'D, 'P>>
    //type private DistributedProcessingMessageInfo<'D, 'P> = MessageInfo<DistributedProcessingMessageData<'D, 'P>>
    //type private DistributedProcessingMessageProcessorProxy<'D, 'P> = MessageProcessorProxy<DistributedProcessingMessageData<'D, 'P>>
    //type private DistributedProcessingResult<'T> = Result<'T, DistributedProcessingError>

    type WorkerNodeMonitorResponse =
        | CannotAccessWrkNode
        | ErrorOccurred of DistributedProcessingError

        override this.ToString() =
            match this with
            | CannotAccessWrkNode -> "Cannot access worker node."
            | ErrorOccurred e -> "Error occurred: " + e.ToString()


    /// The head should contain the latest error and the tail the earliest error.
    let private foldUnitResults r = foldUnitResults DistributedProcessingError.addError r


    let onRegister (proxy : OnRegisterProxy<'D, 'P>) =
        let result =
            {
                partitionerRecipient = proxy.sendMessageProxy.partitionerId
                deliveryType = GuaranteedDelivery
                messageData = proxy.workerNodeInfo |> RegisterWorkerNodePrtMsg
            }.getMessageInfo()
            |> proxy.sendMessageProxy.sendMessage

        result


    let onUnregister (proxy : OnRegisterProxy<'D, 'P>) =
        let result =
            {
                partitionerRecipient = proxy.sendMessageProxy.partitionerId
                deliveryType = GuaranteedDelivery
                messageData = proxy.workerNodeInfo.workerNodeId |> UnregisterWorkerNodePrtMsg
            }.getMessageInfo()
            |> proxy.sendMessageProxy.sendMessage

        result


    /// Now we attempt to restart everything on a [lengthy] timer event. This is to account for NOT
    /// started solvers due to node overload.
    let onStart (proxy : OnStartProxy) =
        match proxy.loadAllActiveRunQueueId() with
        | Ok m -> m |> List.map proxy.onRunModel |> foldUnitResults
        | Error e -> Error e


    let onProcessMessage (proxy : OnProcessMessageProxy<'D>) (m : DistributedProcessingMessage<'D, 'P>) =
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


    //let onGetState (s : WorkerNodeRunnerState) =
    //    failwith "onGetState is not implemented yet."


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


    /// 'D is underlying strongly typed input data, NOT A Message data, 'P is underlying progress data.
    type WorkerNodeRunner<'D, 'P>(i : WorkerNodeRunnerData<'D, 'P>) =
        let mutable callCount = -1
        let mutable started = false

        let incrementCount() = Interlocked.Increment(&callCount)
        let decrementCount() = Interlocked.Decrement(&callCount)

        let onRegisterProxy = onRegisterProxy i

        let onStartProxy =
            {
                loadAllActiveRunQueueId = i.workerNodeProxy.loadAllActiveRunQueueId
                onRunModel = i.workerNodeProxy.onProcessMessageProxy.onRunModel
            }

        let onProcessMessage m =
            let r = onProcessMessage i.workerNodeProxy.onProcessMessageProxy m

            let r1 =
                match r with
                | Ok v -> Ok v
                | Error e ->
                    printfn $"onGetMessagesProxy - error: '{e}'."
                    OnGetMessagesErr FailedToProcessErr |> Error

            r1

        //let onGetMessagesProxy =
        //    {
        //        //tryProcessMessage = onTryProcessMessage i.messageProcessorProxy
        //        onProcessMessage = fun m ->
        //            let r = onProcessMessage i.workerNodeProxy.onProcessMessageProxy m
        //
        //            let r1 =
        //                match r with
        //                | Ok v -> Ok v
        //                | Error e ->
        //                    printfn $"onGetMessagesProxy - error: '{e}'."
        //                    OnGetMessagesErr FailedToProcessErr |> Error
        //
        //            r1
        //        maxMessages = WorkerNodeRunnerState.maxMessages.Length
        //    }
        //
        //let messageLoop =
        //    MailboxProcessor.Start(fun u ->
        //        let rec loop s =
        //            async
        //                {
        //                    match! u.Receive() with
        //                    | Start r -> return! onStart onStartProxy |> r.Reply |> loop
        //                    | Register r -> return! onRegister onRegisterProxy |> r.Reply |> loop
        //                    | Unregister r -> return! onUnregister onRegisterProxy |> r.Reply |> loop
        //                    | GetMessages r -> return! onGetMessages onGetMessagesProxy |> r.Reply |> loop
        //                    //| GetState r -> return! onGetState s |> (withReply r) |> loop
        //                }
        //
        //        WorkerNodeRunnerState.defaultValue |> loop
        //        )

        member _.start() =
            if not started
            then
                let r = onStart onStartProxy
                r
            else Ok()

        member _.register() =
            match onRegister onRegisterProxy with
            | Ok v -> Ok v
            | Error e -> e |> UnableToRegisterWorkerNodeErr |> Error

        member _.unregister() = onUnregister onRegisterProxy

        member _.getMessages() = onGetMessages i.messageProcessorProxy onProcessMessage

        //member _.getState() = messageLoop.PostAndReply GetState
        //
        //member _.onStartProxy =
        //    {
        //        loadAllActiveRunQueueId = i.workerNodeProxy.loadAllActiveRunQueueId
        //        onRunModel = i.workerNodeProxy.onProcessMessageProxy.onRunModel
        //    }
        //
        //member _.onGetMessagesProxy =
        //    {
        //        tryProcessMessage = onTryProcessMessage i.messageProcessorProxy
        //        onProcessMessage = fun w m ->
        //            let r = onProcessMessage i.workerNodeProxy.onProcessMessageProxy m
        //
        //            let r1 =
        //                match r with
        //                | Ok v -> Ok v
        //                | Error e ->
        //                    printfn $"onGetMessagesProxy - error: '{e}'."
        //                    OnGetMessagesErr FailedToProcessErr |> Error
        //
        //            w, r1
        //        maxMessages = WorkerNodeRunnerState.maxMessages
        //    }


    let private createServiceImpl (i : WorkerNodeRunnerData<'D, 'P>) =
        let toError e = failwith $"Error: '{e}'."
        printfn "createServiceImpl: Creating WorkerNodeRunner..."
        let w = WorkerNodeRunner i

        match i.workerNodeServiceInfo.workerNodeInfo.isInactive with
        | false ->
            printfn "createServiceImpl: Registering..."
            match w.register >-> w.start |> evaluate with
            | Ok() ->
                let getMessages() =
                    match w.getMessages() with
                    | Ok () -> Ok ()
                    | Error e -> CreateServiceImplWorkerNodeErr e |> Error

                let i1 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue toError getMessages "WorkerNodeRunner - getMessages"
//                let h = TimerEventHandler(TimerEventHandlerInfo.defaultValue logger getMessages "WorkerNodeRunner - getMessages")
                let h = TimerEventHandler i1
                do h.start()

                // Attempt to restart solvers in case they did not start (due to whatever reason) or got killed.
                let i2 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue toError getMessages "WorkerNodeRunner - start"
                //let s = ClmEventHandler(ClmEventHandlerInfo.oneHourValue logger w.start "WorkerNodeRunner - start")
                let s = TimerEventHandler i2
                do s.start()

                Ok w
            | Error e -> Error e
        | true ->
            printfn "createServiceImpl: Unregistering..."
            match w.unregister() with
            | Ok() -> failwith "createServiceImpl for inactive worker node is not implemented yet."
            | Error e -> CreateServiceImplWorkerNodeErr e |> Error


    type WorkerNodeRunner<'D, 'P>
        with
        static member create messagingDataVersion (i : WorkerNodeServiceInfo) tryRunSolverProcess =
            let logger = Logger.defaultValue
            let getLogger = fun _ -> Logger.defaultValue
            let addError f e = (f + e) |> Error
            let c = getWorkerNodeSvcConnectionString

            let sr n (q : RunQueueId) =
                match tryRunSolverProcess n q with
                | Ok() -> Ok()
                | Error e -> (q |> CannotRunModelErr |> OnRunModelErr) + e |> Error

            let w =
                let messagingClientAccessInfo = i.messagingClientAccessInfo
                let getMessageSize (m : DistributedProcessingMessageData<'D, 'P>) = m.getMessageSize()
                //let x = i.messagingServiceAccessInfo

                let j =
                    {
                        //messagingClientName = WorkerNodeServiceName.netTcpServiceName.value.value |> MessagingClientName
                        storageType = c |> MsSqlDatabase
                        messagingDataVersion = messagingDataVersion
                    }

                let messagingClientData =
                    {
                        msgAccessInfo = messagingClientAccessInfo
                        msgClientProxy = createMessagingClientProxy getLogger getMessageSize j messagingClientAccessInfo.msgClientId
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
                        |> createServiceImpl

                    match n with
                    | Ok v -> Ok v
                    | Error e -> addError UnableToCreateWorkerNodeServiceErr e
                | Error e -> UnableToStartMessagingClientErr e |> Error

            w
