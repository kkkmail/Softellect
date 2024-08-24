namespace Softellect.Messaging

open System.Threading
open CoreWCF
open Softellect.Messaging.Errors
open Softellect.Sys.TimerEvents
open Softellect.Wcf.Common
open Softellect.Wcf.Service
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy

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

        let removeExpiredMessagesImpl () =
            //printfn "removeExpiredMessages was called."
            proxy.deleteExpiredMessages d.messagingServiceAccessInfo.expirationTime

        let createEventHandlers () =
            let info = TimerEventInfo.defaultValue "MessagingService - removeExpiredMessages"

            let proxy =
                {
                    eventHandler = removeExpiredMessagesImpl
                    logger = proxy.logger
                    toErr = fun e -> e |> TimerEventErr
                }

            let i =
                {
                    timerEventInfo = info
                    timerProxy = proxy
                }

            let h = TimerEventHandler i
            do h.start()

        interface IMessagingService<'D> with
            member _.tryStart() =
                createEventHandlers()
                Ok()

            member _.getVersion() =
                printfn "getVersion was called."
                Ok d.messagingServiceAccessInfo.messagingDataVersion

            member _.sendMessage m =
                printfn "sendMessage was called with message: %A." m
                proxy.saveMessage m

            member _.tryPickMessage n =
                printfn "tryPeekMessage was called with MessagingClientId: %A." n
                let result = proxy.tryPickMessage n
                printfn "tryPickMessage - result: %A." result
                result

            member _.tryDeleteFromServer (_, m) =
                //printfn "tryDeleteFromServer was called with MessagingClientId: %A, MessageId: %A." n m
                proxy.deleteMessage m

            member _.removeExpiredMessages() = removeExpiredMessagesImpl()


    type MessagingWcfServiceProxy<'D> =
        {
            logger : MessagingLogger
        }


    type MessagingWcfServiceData<'D> =
        {
            messagingServiceData : MessagingServiceData<'D>
            msgWcfServiceAccessInfo : ServiceAccessInfo
            messagingWcfServiceProxy : MessagingWcfServiceProxy<'D>
        }


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
