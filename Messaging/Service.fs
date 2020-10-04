namespace Softellect.Messaging

open System

open Softellect.Sys.Errors
open Softellect.Sys.MessagingPrimitives
open Softellect.Sys.TimerEvents
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
    let createMessagingServiceEventHandlers logger (w : MessagingService<'D, 'E>) =
        let eventHandler _ = w.removeExpiredMessages()
        let h = TimerEventInfo<'E>.defaultValue logger eventHandler "MessagingService - removeExpiredMessages" |> TimerEventHandler
        do h.start()
