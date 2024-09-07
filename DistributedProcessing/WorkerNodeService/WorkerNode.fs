namespace Softellect.DistributedProcessing.WorkerNodeService

open System
open System.Threading
open System.Threading.Tasks
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
open Softellect.DistributedProcessing.WorkerNodeService.Proxy
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.DataAccess.WorkerNodeService
open Softellect.DistributedProcessing.WorkerNodeService.AppSettings
open Softellect.Sys.Primitives
open Softellect.Sys.Logging
open Softellect.Messaging.Client
open Softellect.Messaging.ServiceProxy
open Microsoft.Extensions.Hosting

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


    //let onRegister (proxy : OnRegisterProxy<'D, 'P>) =
    //    let result =
    //        {
    //            partitionerRecipient = proxy.sendMessageProxy.partitionerId
    //            deliveryType = GuaranteedDelivery
    //            messageData = proxy.workerNodeInfo |> RegisterWorkerNodePrtMsg
    //        }.getMessageInfo()
    //        |> proxy.sendMessageProxy.sendMessage

    //    result


    //let onUnregister (proxy : OnRegisterProxy<'D, 'P>) =
    //    let result =
    //        {
    //            partitionerRecipient = proxy.sendMessageProxy.partitionerId
    //            deliveryType = GuaranteedDelivery
    //            messageData = proxy.workerNodeInfo.workerNodeId |> UnregisterWorkerNodePrtMsg
    //        }.getMessageInfo()
    //        |> proxy.sendMessageProxy.sendMessage

    //    result


    ///// Now we attempt to restart everything on a [lengthy] timer event. This is to account for NOT
    ///// started solvers due to node overload.
    //let onStart (proxy : OnStartProxy) =
    //    match proxy.loadAllActiveRunQueueId() with
    //    | Ok m -> m |> List.map proxy.onRunModel |> foldUnitResults
    //    | Error e -> Error e


    let onProcessMessage (proxy : OnProcessMessageProxy<'D>) (m : DistributedProcessingMessage<'D, 'P>) =
        printfn $"onProcessMessage: Starting. messageId: {m.messageDataInfo.messageId}."

        match m.messageData with
        | UserMsg (WorkerNodeMsg x) ->
            match x with
            | RunModelWrkMsg (r, d) ->
                printfn $"    onProcessMessage: runQueueId: '{r}'."

                match proxy.saveModelData r d with
                | Ok() ->
                    printfn $"    onProcessMessage: saveWorkerNodeRunModelData with runQueueId: '{r}' - OK."
                    let result = proxy.onRunModel r
                    printfn $"    onProcessMessage: onRunModel with runQueueId: '{r}' - %A{result}."
                    result
                | Error e ->
                    printfn $"    onProcessMessage: saveWorkerNodeRunModelData with runQueueId: '{r}' ERROR: %A{e}."
                    let e1 = OnProcessMessageErr (CannotSaveModelDataErr (m.messageDataInfo.messageId, r))
                    e1 + e |> Error
            | CancelRunWrkMsg q -> q ||> proxy.requestCancellation
            | RequestResultWrkMsg q -> q ||> proxy.notifyOfResults
        | _ -> (m.messageDataInfo.messageId, m.messageData.getInfo()) |> InvalidMessageErr |> OnProcessMessageErr |> Error


    //let onGetState (s : WorkerNodeRunnerState) =
    //    failwith "onGetState is not implemented yet."


    //let sendMessageProxy i =
    //    {
    //        partitionerId = i.workerNodeServiceInfo.workerNodeInfo.partitionerId
    //        sendMessage = i.messageProcessor.sendMessage
    //    }


    //let onRegisterProxy i = // : OnRegisterProxy<'D, 'P> =
    //    {
    //        workerNodeInfo = i.workerNodeServiceInfo.workerNodeInfo
    //        sendMessageProxy = sendMessageProxy i
    //    }


    //type IWorkerNodeRunner<'D, 'P> =
    //    abstract member tryStart : unit -> DistributedProcessingUnitResult
    //    abstract member tryStop : unit -> DistributedProcessingUnitResult


    /// 'D is underlying strongly typed input data, NOT A Message data, 'P is underlying progress data.
    type WorkerNodeRunner<'D, 'P>(i : WorkerNodeRunnerData<'D, 'P>) =
        let mutable callCount = -1
        let mutable started = false
        let partitionerId = i.workerNodeServiceInfo.workerNodeInfo.partitionerId
        let messageProcessor = new MessagingClient<DistributedProcessingMessageData<'D, 'P>>(i.messagingClientData) :> IMessageProcessor<DistributedProcessingMessageData<'D, 'P>>

        let incrementCount() = Interlocked.Increment(&callCount)
        let decrementCount() = Interlocked.Decrement(&callCount)

        //let c = getWorkerNodeSvcConnectionString
        //let getLogger = i.messageProcessor.getLogger
        //let onRegisterProxy = onRegisterProxy i

        //let onStartProxy =
        //    {
        //        loadAllActiveRunQueueId = i.workerNodeProxy.loadAllActiveRunQueueId
        //        onRunModel = i.workerNodeProxy.onProcessMessageProxy.onRunModel
        //    }

        let onProcessMessage (m : DistributedProcessingMessage<'D, 'P>) =
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

        let onStartImpl() =
            if not started
            then
                match i.workerNodeProxy.loadAllActiveRunQueueId() with
                | Ok m -> m |> List.map i.workerNodeProxy.onProcessMessageProxy.onRunModel |> foldUnitResults
                | Error e -> Error e
            else Ok()

        let onRegisterImpl() =
            let result =
                {
                    partitionerRecipient = partitionerId
                    deliveryType = GuaranteedDelivery
                    messageData = i.workerNodeServiceInfo.workerNodeInfo |> RegisterWorkerNodePrtMsg
                }.getMessageInfo()
                |> messageProcessor.sendMessage

            match result with
            | Ok v -> Ok v
            | Error e -> e |> UnableToRegisterWorkerNodeErr |> Error

        let onGetMessagesImpl() =
            let r = messageProcessor.processMessages onProcessMessage
            r

        let onUnregisterImpl() = //onUnregister onRegisterProxy
            let result =
                {
                    partitionerRecipient = partitionerId
                    deliveryType = GuaranteedDelivery
                    messageData = i.workerNodeServiceInfo.workerNodeInfo.workerNodeId |> UnregisterWorkerNodePrtMsg
                }.getMessageInfo()
                |> messageProcessor.sendMessage

            result


        let sr n (q : RunQueueId) =
            match i.tryRunSolverProcess n q with
            | Ok() -> Ok()
            | Error e -> (q |> CannotRunModelErr |> OnRunModelErr) + e |> Error

        //let tryStartX() =
        //    //let d = i.
        //    let messagingClientAccessInfo = i.messageProcessorProxy.messagingClientAccessInfo
        //    let getMessageSize (m : DistributedProcessingMessageData<'D, 'P>) = m.getMessageSize()

        //    let j =
        //        {
        //            storageType = c |> MsSqlDatabase
        //            messagingDataVersion = messagingDataVersion
        //        }

        //    let messagingClientData =
        //        {
        //            msgAccessInfo = messagingClientAccessInfo
        //            msgClientProxy = createMessagingClientProxy getLogger getMessageSize j messagingClientAccessInfo.msgClientId
        //            logOnError = true
        //        }

        //    let messagingClient = MessagingClient messagingClientData

        //    messagingClient

        let onTryStart() =
            let toError e = failwith $"Error: '{e}'."

            match i.workerNodeServiceInfo.workerNodeInfo.isInactive with
            | false ->
                printfn "createServiceImpl: Registering..."
                match onRegisterImpl >-> onStartImpl |> evaluate with
                | Ok() ->
                    match messageProcessor.tryStart() with
                    | Ok () ->
                        let getMessages() =
                            match onGetMessagesImpl() with
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

                        Ok ()
                    | Error e -> UnableToStartMessagingClientErr e |> Error 
                | Error e -> Error e
            | true ->
                printfn "createServiceImpl: Unregistering..."
                match onUnregisterImpl() with
                | Ok() -> failwith "createServiceImpl for inactive worker node is not implemented yet."
                | Error e -> CreateServiceImplWorkerNodeErr e |> Error

        let onTryStop() =
            failwith "onTryStop is not implemented yet. Stop all times before exiting."

        //member _.start() = onStartImpl()
        //member _.register() = onRegisterImpl()
        //member _.unregister() = onUnregisterImpl()
        //member _.getMessages() = onGetMessagesImpl()
        //member _.tryStart() = onTryStart()

        //interface IWorkerNodeRunner<'D, 'P> with
        //    member _.tryStart() = onTryStart()
        //    member _.tryStop() = onTryStop()

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                match onTryStart() with
                | Ok () -> Task.CompletedTask
                | Error e -> 
                    printfn $"Error during start: %A{e}."
                    Task.FromException(new Exception($"Failed to start WorkerNodeRunner: %A{e}"))

            member _.StopAsync(cancellationToken: CancellationToken) =
                match onTryStop() with
                | Ok () -> Task.CompletedTask
                | Error e -> 
                    printfn $"Error during stop: %A{e}."
                    Task.CompletedTask // Log the error, but complete the task to allow the shutdown process to continue.

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


//    let private createServiceImpl (i : WorkerNodeRunnerData<'D, 'P>) =
//        let toError e = failwith $"Error: '{e}'."
//        printfn "createServiceImpl: Creating WorkerNodeRunner..."
//        let w = WorkerNodeRunner i
//
////        match i.workerNodeServiceInfo.workerNodeInfo.isInactive with
////        | false ->
////            printfn "createServiceImpl: Registering..."
////            match w.register >-> w.start |> evaluate with
////            | Ok() ->
////                let getMessages() =
////                    match w.getMessages() with
////                    | Ok () -> Ok ()
////                    | Error e -> CreateServiceImplWorkerNodeErr e |> Error
//
////                let i1 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue toError getMessages "WorkerNodeRunner - getMessages"
//////                let h = TimerEventHandler(TimerEventHandlerInfo.defaultValue logger getMessages "WorkerNodeRunner - getMessages")
////                let h = TimerEventHandler i1
////                do h.start()
//
////                // Attempt to restart solvers in case they did not start (due to whatever reason) or got killed.
////                let i2 = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue toError getMessages "WorkerNodeRunner - start"
////                //let s = ClmEventHandler(ClmEventHandlerInfo.oneHourValue logger w.start "WorkerNodeRunner - start")
////                let s = TimerEventHandler i2
////                do s.start()
//
////                Ok w
////            | Error e -> Error e
////        | true ->
////            printfn "createServiceImpl: Unregistering..."
////            match w.unregister() with
////            | Ok() -> failwith "createServiceImpl for inactive worker node is not implemented yet."
////            | Error e -> CreateServiceImplWorkerNodeErr e |> Error
//
//        0


    //type WorkerNodeRunner<'D, 'P>
    //    with
    //    static member create messagingDataVersion (i : WorkerNodeServiceInfo) tryRunSolverProcess =
    //        let logger = Logger.defaultValue
    //        let getLogger = fun _ -> Logger.defaultValue
    //        let addError f e = (f + e) |> Error
    //        let c = getWorkerNodeSvcConnectionString

    //        //let sr n (q : RunQueueId) =
    //        //    match tryRunSolverProcess n q with
    //        //    | Ok() -> Ok()
    //        //    | Error e -> (q |> CannotRunModelErr |> OnRunModelErr) + e |> Error

    //        let w =
    //            //let messagingClientAccessInfo = i.messagingClientAccessInfo
    //            //let getMessageSize (m : DistributedProcessingMessageData<'D, 'P>) = m.getMessageSize()

    //            //let j =
    //            //    {
    //            //        storageType = c |> MsSqlDatabase
    //            //        messagingDataVersion = messagingDataVersion
    //            //    }

    //            //let messagingClientData =
    //            //    {
    //            //        msgAccessInfo = messagingClientAccessInfo
    //            //        msgClientProxy = createMessagingClientProxy getLogger getMessageSize j messagingClientAccessInfo.msgClientId
    //            //        logOnError = true
    //            //    }

    //            //let messagingClient = MessagingClient messagingClientData

    //            match messagingClient.tryStart() with
    //            | Ok() ->
    //                let n =
    //                    {
    //                        workerNodeServiceInfo = i
    //                        workerNodeProxy = WorkerNodeProxy<'D>.create c (sr i.workerNodeInfo.noOfCores)
    //                        messageProcessorProxy = messagingClient.messageProcessorProxy
    //                        tryRunSolverProcess = tryRunSolverProcess
    //                    }
    //                    |> createServiceImpl

    //                match n with
    //                | Ok v -> Ok v
    //                | Error e -> addError UnableToCreateWorkerNodeServiceErr e
    //            | Error e -> UnableToStartMessagingClientErr e |> Error

    //        w
