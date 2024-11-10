namespace Softellect.Messaging

open System
open System.Threading
open System.Threading.Tasks
open CoreWCF
open Softellect.Messaging.Errors
open Softellect.Sys.TimerEvents
open Softellect.Wcf.Service
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy
open Microsoft.Extensions.Hosting

module Service =

    type MessagingServiceData<'D> =
        {
            messagingServiceProxy : MessagingServiceProxy<'D>
            messagingServiceAccessInfo : MessagingServiceAccessInfo
        }


    let mutable private messagingServiceCount = 0L


    type MessagingService<'D> (d : MessagingServiceData<'D>) =
        let count = Interlocked.Increment(&messagingServiceCount)
        do printfn $"MessagingService: count = {count}."
        let proxy = d.messagingServiceProxy
        let mutable started = false
        let mutable eventHandler = None

        let removeExpiredMessagesImpl () =
            //printfn "removeExpiredMessages was called."
            proxy.deleteExpiredMessages d.messagingServiceAccessInfo.expirationTime

        let createEventHandlers () =
            let info = TimerEventInfo.defaultValue "MessagingService - removeExpiredMessages"

            let proxy =
                {
                    eventHandler = removeExpiredMessagesImpl
                    getLogger = proxy.getLogger
                    toErr = fun e -> e |> TimerEventErr
                }

            let i =
                {
                    timerEventInfo = info
                    timerProxy = proxy
                }

            let h = TimerEventHandler i
            do h.start()
            Some h

        let onTryStart() =
            if started then
                printfn "Already started."
                Ok() // Don't care if it is started.
            else
                eventHandler <- createEventHandlers()
                started <- true
                Ok()

        let onTryStop() =
            if not started then
                printfn "Already stopped."
                Ok() // Don't care if it is already stopped.
            else
                started <- false

                match eventHandler with
                | Some h ->
                    h.stop()
                    Ok()
                | None -> Ok()

        interface IMessagingService<'D> with
            member _.getVersion() =
                printfn "getVersion was called."
                Ok d.messagingServiceAccessInfo.messagingDataVersion

            member _.sendMessage m =
                printfn $"sendMessage was called with message: %A{m}."
                let result = proxy.saveMessage m
                printfn $"sendMessage - result: %A{result}."
                result

            member _.tryPickMessage n =
                printfn $"tryPeekMessage was called with MessagingClientId: %A{n}."
                let result = proxy.tryPickMessage n
                printfn $"tryPickMessage - result: %A{result}."
                result

            member _.tryDeleteFromServer (_, m) =
                //printfn "tryDeleteFromServer was called with MessagingClientId: %A, MessageId: %A." n m
                proxy.deleteMessage m

        interface IHostedService with
            member _.StartAsync(cancellationToken: CancellationToken) =
                match onTryStart() with
                | Ok () ->
                    printfn "MessagingService is starting..."
                    Task.CompletedTask
                | Error e ->
                    printfn $"Error during start: %A{e}."
                    Task.FromException(Exception($"Failed to start WorkerNodeRunner: %A{e}"))

            member _.StopAsync(cancellationToken: CancellationToken) =
                match onTryStop() with
                | Ok () ->
                    printfn "MessagingService is stopping..."
                    Task.CompletedTask
                | Error e ->
                    printfn $"Error during stop: %A{e}."
                    Task.CompletedTask // Log the error, but complete the task to allow the shutdown process to continue.


    let mutable private serviceCount = 0L


    [<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, IncludeExceptionDetailInFaults = true)>]
    type MessagingWcfService<'D> (m : IMessagingService<'D>) =
        let count = Interlocked.Increment(&serviceCount)
        do printfn $"MessagingWcfService: count = {count}."

        let toGetVersionError f = f |> GetVersionSvcWcfErr |> GetVersionSvcErr
        let toSendMessageError f = f |> MsgWcfErr |> MessageDeliveryErr
        let toTryPickMessageError f = f |> TryPickMsgWcfErr |> TryPickMessageWcfErr
        let toTryDeleteFromServerError f = f |> TryDeleteFromServerWcfErr |> TryDeleteFromServerErr

        interface IMessagingWcfService with
            member _.getVersion b = tryReply m.getVersion toGetVersionError b
            member _.sendMessage b = tryReply m.sendMessage toSendMessageError b
            member _.tryPickMessage b = tryReply m.tryPickMessage toTryPickMessageError b
            member _.tryDeleteFromServer b = tryReply m.tryDeleteFromServer toTryDeleteFromServerError b
