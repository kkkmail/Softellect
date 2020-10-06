namespace Softellect.Messaging

open System

open CoreWCF

open Softellect.Sys
open Softellect.Sys.Errors
open Softellect.Sys.MessagingServiceErrors
open Softellect.Sys.MessagingPrimitives
open Softellect.Sys.TimerEvents
open Softellect.Wcf.Service
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
        member w.createMessagingServiceEventHandlers logger =
            let eventHandler _ = w.removeExpiredMessages()
            let h = TimerEventInfo<'E>.defaultValue logger eventHandler "MessagingService - removeExpiredMessages" |> TimerEventHandler
            do h.start()


    ////[<ServiceBehavior(IncludeExceptionDetailInFaults = true, InstanceContextMode = InstanceContextMode.PerSession)>]
    //type MessagingWcfService<'D, 'E>() =
    //    static let tryCreateMessagingWcfService(i : MessagingServiceInfo) : StlResult<MessagingWcfService<'D, 'E>, 'E> =
    //        failwith ""

    //    static let messagingService : Lazy<StlResult<MessagingService<'D, 'E>, 'E>> =
    //        new Lazy<StlResult<MessagingService<'D, 'E>, 'E>>(fun () -> 0)
    //        //tryCreateWebHostBuilder WcfServiceAccessInfo<'S>.serviceAccessInfo)

    //    let toGetVersionError f = f |> GetVersionSvcWcfErr |> GetVersionSvcErr |> MessagingServiceErr
    //    let toSendMessageError f = f |> MsgWcfErr |> MessageDeliveryErr |> MessagingServiceErr
    //    let toTryPickMessageError f = f |> TryPeekMsgWcfErr |> TryPeekMessageErr |> MessagingServiceErr
    //    let toTryDeleteFromServerError f = f |> TryDeleteMsgWcfErr |> TryDeleteFromServerErr |> MessagingServiceErr

    //    let getVersion() = messagingService.Value |> Rop.bind (fun e -> e.getVersion())
    //    let sendMessage b = messagingService.Value |> Rop.bind (fun e -> e.sendMessage b)
    //    let tryPeekMessage b = messagingService.Value |> Rop.bind (fun e -> e.tryPeekMessage b)
    //    let tryDeleteFromServer b = messagingService.Value |> Rop.bind (fun e -> e.tryDeleteFromServer b)

    //    interface IMessagingWcfService with
    //        member _.getVersion b = tryReply getVersion toGetVersionError b
    //        member _.sendMessage b = tryReply sendMessage toSendMessageError b
    //        member _.tryPeekMessage b = tryReply tryPeekMessage toTryPickMessageError b
    //        member _.tryDeleteFromServer b = tryReply tryDeleteFromServer toTryDeleteFromServerError b
