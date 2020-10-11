namespace Softellect.Messaging

open System

open CoreWCF

open Softellect.Sys
open Softellect.Sys.WcfErrors
open Softellect.Sys.Logging
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

    type MessagingServiceInfo =
        {
            expirationTime : TimeSpan
            messagingDataVersion : MessagingDataVersion
        }

        static member defaultExpirationTime = TimeSpan.FromMinutes 5.0

        static member defaultValue v =
            {
                expirationTime = MessagingServiceInfo.defaultExpirationTime
                messagingDataVersion = v
            }

        static member defaultValue() =
            {
                expirationTime = MessagingServiceInfo.defaultExpirationTime
                messagingDataVersion = MessagingDataVersion 0
            }


    type MessagingServiceData<'D, 'E> =
        {
            messagingServiceInfo : MessagingServiceInfo
            messagingServiceProxy : MessagingServiceProxy<'D, 'E>
        }

        static member defaultValue : MessagingServiceData<'D, 'E> =
            {
                messagingServiceInfo = MessagingServiceInfo.defaultValue()
                messagingServiceProxy = MessagingServiceProxy.defaultValue
            }


    type MessagingService<'D, 'E> private (d : MessagingServiceData<'D, 'E>) =
        static let mutable getData : unit -> MessagingServiceData<'D, 'E> option =
            fun () -> None

        //static let mutable serviceDataOpt : MessagingServiceData<'D, 'E> option = None
        //static let serviceOpt : Lazy<MessagingService<'D, 'E> option> = failwith ""

        static let createService() : ResultWithErr<MessagingService<'D, 'E>, 'E> =
            match getData() with
            | Some data ->
                let service = MessagingService<'D, 'E>(data)
                service.createEventHandlers()
                Ok service
            | None ->
                printfn "Data is unavailable."
                failwith ""

        let proxy = d.messagingServiceProxy

        member _.getVersion() : ResultWithErr<MessagingDataVersion, 'E> = Ok d.messagingServiceInfo.messagingDataVersion
        member _.sendMessage (m : Message<'D>) : UnitResult<'E> = proxy.saveMessage m
        member _.tryPeekMessage (n : MessagingClientId) : ResultWithErr<Message<'D> option, 'E> = proxy.tryPickMessage n
        member _.tryDeleteFromServer (n : MessagingClientId, m : MessageId) : UnitResult<'E> = proxy.deleteMessage m
        member _.removeExpiredMessages() : UnitResult<'E> = proxy.deleteExpiredMessages d.messagingServiceInfo.expirationTime

        /// Call this function to create timer events necessary for automatic Messaging Service operation.
        /// If you don't call it, then you have to operate Messaging Service by hands.
        member w.createEventHandlers () =
            let eventHandler _ = w.removeExpiredMessages()
            let info = TimerEventInfo.defaultValue "MessagingService - removeExpiredMessages"

            let proxy =
                {
                    eventHandler = eventHandler
                    logger = proxy.logger
                    toErr = fun e -> e |> TimerEventErr |> proxy.toErr
                }

            let h = TimerEventHandler(info, proxy)
            do h.start()

        static member service =
            new Lazy<ResultWithErr<MessagingService<'D, 'E>, 'E>>(createService)

        static member setGetData g = getData <- g


    //type MessagingWcfServiceAccessInfo =
    //    {
    //        msgWcfServiceAccessInfo : WcfServiceAccessInfo
    //    }
    //
    //    static member tryCreate (i : MessagingServiceAccessInfo) =
    //        match WcfServiceAccessInfo.tryCreate i.messagingServiceAccessInfo with
    //        | Ok r ->
    //            {
    //                msgWcfServiceAccessInfo = r
    //            }
    //            |> Ok
    //        | Error e -> Error e
    //
    //
    //type MessagingWcfServiceProxy =
    //    {
    //        msgWcfServiceAccessInfoRes : WcfResult<MessagingWcfServiceAccessInfo>
    //        loggerOpt : WcfLogger option
    //    }
    //
    //    static member defaultValue =
    //        {
    //            msgWcfServiceAccessInfoRes = WcfServiceNotInitializedErr |> SingleErr |> Error
    //            loggerOpt = None
    //        }
    //
    //    member proxy.tryGetWcfServiceProxy() =
    //        match proxy.msgWcfServiceAccessInfoRes with
    //        | Ok r ->
    //            {
    //                wcfServiceAccessInfoRes = Ok r.msgWcfServiceAccessInfo
    //                loggerOpt = proxy.loggerOpt
    //            }
    //            |> Ok
    //        | Error e -> Error e
    //
    //
    //type MessagingWcfServiceProxy<'D, 'E>() =
    //    static let mutable serviceProxy = MessagingWcfServiceProxy.defaultValue
    //    static member setProxy proxy = serviceProxy <- proxy
    //    static member proxy = serviceProxy
    //
    //    member _.serviceTypes = (typeof<'D>, typeof<'E>)


    type MessagingWcfServiceProxy<'D, 'E> =
        {
            logger : Logger<'E>
            toErr : MessagingServiceError -> Err<'E>
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
                        toErr = fun e -> failwith (sprintf "%A" e)
                    }
            }


    //[<ServiceBehavior(IncludeExceptionDetailInFaults = true, InstanceContextMode = InstanceContextMode.PerSession)>]
    type MessagingWcfService<'D, 'E> private (d : MessagingWcfServiceData<'D, 'E>) =
        static let tryGetData() =
            WcfServiceData<MessagingWcfService<'D, 'E>, MessagingWcfServiceData<'D, 'E>>.dataOpt

        static let tryCreateService() : MessagingWcfService<'D, 'E> option = failwith ""

        let proxy = d.messagingWcfServiceProxy
        let messagingService =
            MessagingService<'D, 'E>.service
        //do messagingService.createEventHandlers()


        //static let tryCreateMessagingWcfService (proxy : MessagingWcfServiceProxy<'D, 'E>) : MessagingWcfService<'D, 'E> =
        //    failwith ""

        //static let tryCreateMessagingService(i : MessagingWcfServiceProxy<'D, 'E>) : ResultWithErr<MessagingService<'D, 'E>, 'E> =
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

        //let messagingService : MessagingService<'D, 'E> = failwith "" // MessagingService<'D, 'E>()

        //static let service : Lazy<ResultWithErr<MessagingWcfService<'D, 'E>, 'E>> = failwith ""

        let toGetVersionError f = f |> GetVersionSvcWcfErr |> GetVersionSvcErr |> proxy.toErr
        let toSendMessageError f = f |> MsgWcfErr |> MessageDeliveryErr |> proxy.toErr
        let toTryPickMessageError f = f |> TryPeekMsgWcfErr |> TryPeekMessageErr |> proxy.toErr
        let toTryDeleteFromServerError f = f |> TryDeleteMsgWcfErr |> TryDeleteFromServerErr |> proxy.toErr

        let getVersion() = messagingService.Value |> Rop.bind (fun e -> e.getVersion())
        let sendMessage b = messagingService.Value |> Rop.bind (fun e -> e.sendMessage b)
        let tryPeekMessage b = messagingService.Value |> Rop.bind (fun e -> e.tryPeekMessage b)
        let tryDeleteFromServer b = messagingService.Value |> Rop.bind (fun e -> e.tryDeleteFromServer b)

        interface IMessagingWcfService with
            member _.getVersion b =
                tryReply getVersion toGetVersionError b
            member _.sendMessage b =
                tryReply sendMessage toSendMessageError b
            member _.tryPeekMessage b =
                tryReply tryPeekMessage toTryPickMessageError b
            member _.tryDeleteFromServer b =
                tryReply tryDeleteFromServer toTryDeleteFromServerError b

        //static member setProxy proxy = MessagingWcfServiceProxy<'D, 'E>.setProxy proxy
        //static member tryGetService() = service.Value

        //static member tryGetService proxy =
        //    MessagingWcfService<'D, 'E>.setProxy proxy
        //    service.Value

        new() = MessagingWcfService<'D, 'E> (tryGetData()
                                                |> Option.bind (fun e -> Some e.serviceData)
                                                |> Option.defaultValue MessagingWcfServiceData<'D, 'E>.defaultValue)

        //member x.run() =
        //    ignore()
