namespace Softellect.Messaging

open System

open System.Threading
open System
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open CoreWCF
open Softellect.Sys
open Softellect.Sys.Logging
open Softellect.Messaging.Errors
open Softellect.Messaging.Primitives
open Softellect.Sys.TimerEvents
open Softellect.Wcf.Common
open Softellect.Wcf.Errors
open Softellect.Wcf.Service
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy

module Service =

    type MessagingServiceInfo =
        {
            expirationTime : TimeSpan
            messagingDataVersion : MessagingDataVersion
        }

        static member defaultExpirationTime = TimeSpan.FromMinutes 5.0

        static member getDefaultValue v =
            {
                expirationTime = MessagingServiceInfo.defaultExpirationTime
                messagingDataVersion = v
            }

        static member getDefaultValue() =
            {
                expirationTime = MessagingServiceInfo.defaultExpirationTime
                messagingDataVersion = MessagingDataVersion 0
            }


    type MessagingServiceData<'D> =
        {
            messagingServiceInfo : MessagingServiceInfo
            communicationType : WcfCommunicationType
            messagingServiceProxy : MessagingServiceProxy<'D>
        }

        static member defaultValue : MessagingServiceData<'D> =
            {
                messagingServiceInfo = MessagingServiceInfo.getDefaultValue()
                communicationType = HttpCommunication
                messagingServiceProxy = MessagingServiceProxy.defaultValue
            }


    type WcfServiceDataResult<'D> = Result<WcfServiceData<MessagingServiceData<'D>>, WcfError>


    let mutable messagingServiceCount = 0L


    //type MessagingService<'D> private (d : MessagingServiceData<'D>) =
    //    let count = Interlocked.Increment(&messagingServiceCount)
    //    do printfn $"MessagingService: count = {count}."
    //    static let mutable getData : unit -> MessagingServiceData<'D> option = fun () -> None

    //    static let createService() : Result<IMessagingService<'D>, MessagingError> =
    //        match getData() with
    //        | Some data ->
    //            let service = MessagingService<'D>(data)
    //            service.createEventHandlers()
    //            service :> IMessagingService<'D> |> Ok
    //        | None ->
    //            let errMessage = $"MessagingService<%s{typedefof<'D>.Name}>: Data is unavailable."
    //            printfn $"%s{errMessage}"
    //            failwith errMessage

    //    static let serviceImpl = new Lazy<Result<IMessagingService<'D>, MessagingError>>(createService)
    //    let proxy = d.messagingServiceProxy

    //    static member getService() = serviceImpl.Value // new Lazy<Result<IMessagingService<'D, 'E>, 'E>>(createService)
    //    static member setGetData g = getData <- g

    //    static member tryStart() = MessagingService<'D>.getService() |> Rop.bind (fun _ -> Ok())

    //    interface IMessagingService<'D> with
    //        member _.getVersion() =
    //            printfn "getVersion was called."
    //            Ok d.messagingServiceInfo.messagingDataVersion

    //        member _.sendMessage m =
    //            printfn "sendMessage was called with message: %A." m
    //            proxy.saveMessage m

    //        member _.tryPickMessage n =
    //            printfn "tryPeekMessage was called with MessagingClientId: %A." n
    //            let result = proxy.tryPickMessage n
    //            printfn "tryPickMessage - result: %A." result
    //            result

    //        member _.tryDeleteFromServer (_, m) =
    //            //printfn "tryDeleteFromServer was called with MessagingClientId: %A, MessageId: %A." n m
    //            proxy.deleteMessage m

    //        member _.removeExpiredMessages() =
    //            //printfn "removeExpiredMessages was called."
    //            proxy.deleteExpiredMessages d.messagingServiceInfo.expirationTime

    //    member private w.createEventHandlers () =
    //        let eventHandler _ = (w :> IMessagingService<'D>).removeExpiredMessages()
    //        let info = TimerEventInfo.defaultValue "MessagingService - removeExpiredMessages"

    //        let proxy =
    //            {
    //                eventHandler = eventHandler
    //                logger = proxy.logger
    //                toErr = fun e -> e |> TimerEventErr
    //            }

    //        let i =
    //            {
    //                timerEventInfo = info
    //                timerProxy = proxy
    //            }

    //        let h = TimerEventHandler i
    //        do h.start()


    type MessagingService<'D> (d : MessagingServiceData<'D>) =
        let count = Interlocked.Increment(&messagingServiceCount)
        do printfn $"MessagingService: count = {count}."
        let proxy = d.messagingServiceProxy

        let removeExpiredMessagesImpl () =
            //printfn "removeExpiredMessages was called."
            proxy.deleteExpiredMessages d.messagingServiceInfo.expirationTime

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
                Ok d.messagingServiceInfo.messagingDataVersion

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
            msgWcfServiceAccessInfo : WcfServiceAccessInfo
            messagingWcfServiceProxy : MessagingWcfServiceProxy<'D>
        }

        static member defaultValue : MessagingWcfServiceData<'D> =
            {
                messagingServiceData = MessagingServiceData.defaultValue
                msgWcfServiceAccessInfo = WcfServiceAccessInfo.defaultValue

                messagingWcfServiceProxy =
                    {
                        logger = Logger.defaultValue
                    }
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


    /// Tries to create MessagingWcfServiceData needed for MessagingWcfService.
    let tryGetMsgServiceData<'D> serviceAccessInfo wcfLogger (messagingServiceData : MessagingServiceData<'D>) =
        let retVal =
            match WcfServiceAccessInfo.tryCreate serviceAccessInfo with
            | Ok i ->
                {
                    wcfServiceAccessInfo = i

                    wcfServiceProxy =
                        {
                            wcfLogger = wcfLogger
                        }

                    serviceData = messagingServiceData
                }
                |> Ok
            | Error e -> Error e

        printfn $"tryGetMsgServiceData: retVal = %A{retVal}"
        retVal
