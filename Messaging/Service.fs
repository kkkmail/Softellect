namespace Softellect.Messaging

open System

open CoreWCF

open Softellect.Sys
open Softellect.Sys.WcfErrors
open Softellect.Sys.Errors
open Softellect.Sys.MessagingServiceErrors
open Softellect.Sys.MessagingPrimitives
open Softellect.Sys.TimerEvents
open Softellect.Wcf.Service
open Softellect.Wcf.Common
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Primitives
open Softellect.Messaging.Proxy

module Service =

    type MessagingServiceData<'D, 'E> =
        {
            messagingServiceProxy : MessagingServiceProxy<'D, 'E>
            expirationTime : TimeSpan
            messagingDataVersion : MessagingDataVersion
        }

        static member defaultExpirationTime = TimeSpan.FromMinutes 5.0


    type MessagingService<'D, 'E>(d : MessagingServiceData<'D, 'E>) =
        let proxy = d.messagingServiceProxy

        member _.getVersion() : StlResult<MessagingDataVersion, 'E> = Ok d.messagingDataVersion
        member _.sendMessage (m : Message<'D>) : UnitResult<'E> = proxy.saveMessage m
        member _.tryPeekMessage (n : MessagingClientId) : StlResult<Message<'D> option, 'E> = proxy.tryPickMessage n
        member _.tryDeleteFromServer (n : MessagingClientId, m : MessageId) : UnitResult<'E> = proxy.deleteMessage m
        member _.removeExpiredMessages() : UnitResult<'E> = proxy.deleteExpiredMessages d.expirationTime

        /// Call this function to create timer events necessary for automatic Messaging Service operation.
        /// If you don't call it, then you have to operate Messaging Service by hands.
        member w.createEventHandlers logger =
            let eventHandler _ = w.removeExpiredMessages()
            let h = TimerEventInfo<'E>.defaultValue logger eventHandler "MessagingService - removeExpiredMessages" |> TimerEventHandler
            do h.start()


    type MessagingWcfServiceAccessInfo =
        {
            msgWcfServiceAccessInfo : WcfServiceAccessInfo
        }

        static member tryCreate (i : MessagingServiceAccessInfo) =
            match WcfServiceAccessInfo.tryCreate i.messagingServiceAccessInfo with
            | Ok r ->
                {
                    msgWcfServiceAccessInfo = r
                }
                |> Ok
            | Error e -> Error e


    type MessagingWcfServiceProxy =
        {
            msgWcfServiceAccessInfoRes : WcfResult<MessagingWcfServiceAccessInfo>
            loggerOpt : WcfLogger option
        }

        static member defaultValue = 
            {
                msgWcfServiceAccessInfoRes = WcfServiceNotInitializedErr |> Error
                loggerOpt = None
            }

        member proxy.tryGetWcfServiceProxy() =
            match proxy.msgWcfServiceAccessInfoRes with
            | Ok r ->
                {
                    wcfServiceAccessInfoRes = Ok r.msgWcfServiceAccessInfo
                    loggerOpt = proxy.loggerOpt
                }
                |> Ok
            | Error e -> Error e


    type MessagingWcfServiceProxy<'D, 'E>() =
        static let mutable serviceProxy :  MessagingWcfServiceProxy<'D, 'E> = MessagingWcfServiceProxy.defaultValue
        static member setProxy proxy = serviceProxy <- proxy
        static member proxy = serviceProxy

        member _.serviceTypes = (typeof<'D>, typeof<'E>)


    //[<ServiceBehavior(IncludeExceptionDetailInFaults = true, InstanceContextMode = InstanceContextMode.PerSession)>]
    type MessagingWcfService<'D, 'E>() =
        static let tryCreateMessagingWcfService (proxy : MessagingWcfServiceProxy<'D, 'E>) : MessagingWcfService<'D, 'E> =
            failwith ""

        //static let tryCreateMessagingService(i : MessagingWcfServiceProxy) : StlResult<MessagingService<'D, 'E>, 'E> =
        //    match i.tryGetWcfServiceProxy() with
        //    | Ok r ->
        //        match 
        //        WcfServiceProxy<'D>.setProxy r
        //        let wcfService = WcfService<MessagingWcfService<'D, 'E>, IMessagingWcfService>
        //        failwith ""

        //    | Error e -> Error e

        //static let messagingService : Lazy<StlResult<MessagingService<'D, 'E>, 'E>> =
        //    new Lazy<StlResult<MessagingService<'D, 'E>, 'E>>(fun () -> tryCreateMessagingService MessagingWcfServiceProxy<'D, 'E>.proxy)
        ////    //tryCreateWebHostBuilder WcfServiceAccessInfo<'S>.serviceAccessInfo)

        let messagingService : MessagingService<'D, 'E> = MessagingService<'D, 'E>()

        static let service : Lazy<StlResult<MessagingWcfService<'D, 'E>, 'E>> = failwith ""

        let toGetVersionError f = f |> GetVersionSvcWcfErr |> GetVersionSvcErr |> MessagingServiceErr
        let toSendMessageError f = f |> MsgWcfErr |> MessageDeliveryErr |> MessagingServiceErr
        let toTryPickMessageError f = f |> TryPeekMsgWcfErr |> TryPeekMessageErr |> MessagingServiceErr
        let toTryDeleteFromServerError f = f |> TryDeleteMsgWcfErr |> TryDeleteFromServerErr |> MessagingServiceErr

        interface IMessagingWcfService with
            member _.getVersion b = tryReply messagingService.getVersion toGetVersionError b
            member _.sendMessage b = tryReply messagingService.sendMessage toSendMessageError b
            member _.tryPeekMessage b = tryReply messagingService.tryPeekMessage toTryPickMessageError b
            member _.tryDeleteFromServer b = tryReply messagingService.tryDeleteFromServer toTryDeleteFromServerError b

        static member setProxy (proxy : MessagingWcfServiceProxy<'D, 'E>) = MessagingWcfServiceProxy<'D, 'E>.setProxy proxy
        static member tryGetService() = service.Value

        static member tryGetService proxy =
            MessagingWcfService<'D, 'E>.setProxy proxy
            service.Value
