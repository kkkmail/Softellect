namespace Softellect.Messaging

open System

open Softellect.Sys
open Softellect.Sys.Logging
open Softellect.Sys.MessagingServiceErrors
open Softellect.Sys.MessagingPrimitives
open Softellect.Sys.TimerEvents
open Softellect.Wcf.Common
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


    type MessagingServiceData<'D, 'E> =
        {
            messagingServiceInfo : MessagingServiceInfo
            communicationType : WcfCommunicationType
            messagingServiceProxy : MessagingServiceProxy<'D, 'E>
        }

        static member defaultValue : MessagingServiceData<'D, 'E> =
            {
                messagingServiceInfo = MessagingServiceInfo.getDefaultValue()
                communicationType = HttpCommunication
                messagingServiceProxy = MessagingServiceProxy.defaultValue
            }


    type MessagingService<'D, 'E> private (d : MessagingServiceData<'D, 'E>) =
        static let mutable getData : unit -> MessagingServiceData<'D, 'E> option = fun () -> None

        static let createService() : Result<IMessagingService<'D, 'E>, 'E> =
            match getData() with
            | Some data ->
                let service = MessagingService<'D, 'E>(data)
                service.createEventHandlers()
                service :> IMessagingService<'D, 'E> |> Ok
            | None ->
                let errMessage = $"MessagingService<%s{typedefof<'D>.Name}, %s{typedefof<'E>.Name}>: Data is unavailable."
                printfn $"%s{errMessage}"
                failwith errMessage

        let proxy = d.messagingServiceProxy

        static member service = new Lazy<Result<IMessagingService<'D, 'E>, 'E>>(createService)
        static member setGetData g = getData <- g

        static member tryStart() = MessagingService<'D, 'E>.service.Value |> Rop.bind (fun _ -> Ok())

        interface IMessagingService<'D, 'E> with
            member _.getVersion() =
                //printfn "getVersion was called."
                Ok d.messagingServiceInfo.messagingDataVersion

            member _.sendMessage m =
                //printfn "sendMessage was called with message: %A." m
                proxy.saveMessage m

            member _.tryPeekMessage n =
                //printfn "tryPeekMessage was called with MessagingClientId: %A." n
                let result = proxy.tryPickMessage n
                //printfn "tryPeekMessage - result: %A." result
                result

            member _.tryDeleteFromServer (_, m) =
                //printfn "tryDeleteFromServer was called with MessagingClientId: %A, MessageId: %A." n m
                proxy.deleteMessage m

            member _.removeExpiredMessages() =
                //printfn "removeExpiredMessages was called."
                proxy.deleteExpiredMessages d.messagingServiceInfo.expirationTime

        member private w.createEventHandlers () =
            let eventHandler _ = (w :> IMessagingService<'D, 'E>).removeExpiredMessages()
            let info = TimerEventInfo.defaultValue "MessagingService - removeExpiredMessages"

            let proxy =
                {
                    eventHandler = eventHandler
                    logger = proxy.logger
                    toErr = fun e -> e |> TimerEventErr |> proxy.toErr
                }

            let h = TimerEventHandler(info, proxy)
            do h.start()


    type MessagingWcfServiceProxy<'D, 'E> =
        {
            logger : Logger<'E>
            toErr : MessagingServiceError -> 'E
        }


    type MessagingWcfServiceData<'D, 'E> =
        {
            messagingServiceData : MessagingServiceData<'D, 'E>
            msgWcfServiceAccessInfo : WcfServiceAccessInfo
            messagingWcfServiceProxy : MessagingWcfServiceProxy<'D, 'E>
        }

        static member defaultValue : MessagingWcfServiceData<'D, 'E> =
            {
                messagingServiceData = MessagingServiceData.defaultValue
                msgWcfServiceAccessInfo = WcfServiceAccessInfo.defaultValue

                messagingWcfServiceProxy =
                    {
                        logger = Logger.defaultValue
                        toErr =
                            fun e -> failwith $"MessagingWcfServiceData<%s{typedefof<'D>.Name}, %s{typedefof<'E>.Name}>: Error occurred: %A{e}."
                    }
            }


    type MessagingWcfService<'D, 'E> private (d : MessagingWcfServiceData<'D, 'E>) =
        static let getServiceData() =
            getData<MessagingWcfService<'D, 'E>, MessagingWcfServiceData<'D, 'E>> MessagingWcfServiceData<'D, 'E>.defaultValue

        let proxy = d.messagingWcfServiceProxy
        let messagingService = MessagingService<'D, 'E>.service

        let toGetVersionError f = f |> GetVersionSvcWcfErr |> GetVersionSvcErr |> proxy.toErr
        let toSendMessageError f = f |> MsgWcfErr |> MessageDeliveryErr |> proxy.toErr
        let toTryPickMessageError f = f |> TryPeekMsgWcfErr |> TryPeekMessageErr |> proxy.toErr
        let toTryDeleteFromServerError f = f |> TryDeleteMsgWcfErr |> TryDeleteFromServerErr |> proxy.toErr

        let getVersion() = messagingService.Value |> Rop.bind (fun e -> e.getVersion())
        let sendMessage b = messagingService.Value |> Rop.bind (fun e -> e.sendMessage b)
        let tryPeekMessage b = messagingService.Value |> Rop.bind (fun e -> e.tryPeekMessage b)
        let tryDeleteFromServer b = messagingService.Value |> Rop.bind (fun e -> e.tryDeleteFromServer b)

        new() = MessagingWcfService<'D, 'E> (getServiceData())

        interface IMessagingWcfService with
            member _.getVersion b = tryReply getVersion toGetVersionError b
            member _.sendMessage b = tryReply sendMessage toSendMessageError b
            member _.tryPeekMessage b = tryReply tryPeekMessage toTryPickMessageError b
            member _.tryDeleteFromServer b = tryReply tryDeleteFromServer toTryDeleteFromServerError b


    /// Tries to create MessagingWcfServiceData needed for MessagingWcfService.
    let tryGetMsgServiceData<'D, 'E> serviceAccessInfo wcfLogger messagingServiceData =
        match WcfServiceAccessInfo.tryCreate serviceAccessInfo  with
        | Ok i ->
            {
                wcfServiceAccessInfo = i

                wcfServiceProxy =
                    {
                        wcfLogger = wcfLogger
                    }

                serviceData = messagingServiceData
                setData = fun e -> MessagingService<'D, 'E>.setGetData (fun () -> Some e)
            }
            |> Ok
        | Error e -> Error e
