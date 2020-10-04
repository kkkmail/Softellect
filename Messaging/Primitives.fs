namespace Softellect.Messaging

open System
open Softellect.Sys.MessagingPrimitives

module Primitives =

    [<Literal>]
    let MessagingWcfServiceName = "MessagingWcfService"


    type MessageType =
        | IncomingMessage
        | OutgoingMessage


    type MessageDeliveryType =
        | GuaranteedDelivery
        | NonGuaranteedDelivery

        member d.value =
            match d with
            | GuaranteedDelivery -> 0
            | NonGuaranteedDelivery -> 1

        static member tryCreate i =
            match i with
            | 0 -> Some GuaranteedDelivery
            | 1 -> Some NonGuaranteedDelivery
            | _ -> None


    type MessageSize =
        | SmallSize
        | MediumSize
        | LargeSize


    type MessageData<'M> =
        | TextMsg of string
        | OtherMsg of 'M

        static member maxInfoLength = 500

        member this.getMessageSize() = failwith ""
        //    match this with
        //    | TextMessage s ->
        //        if s.Length < 1_000 then SmallSize
        //        else if s.Length < 1_000_000 then MediumSize
        //        else LargeSize
        //    | Message m -> m.messageSize

        member this.keepInMemory() =
            match this.getMessageSize() with
            | SmallSize -> true
            | MediumSize -> false
            | LargeSize -> false

        member this.getInfo() =
            let s = (sprintf "%A" this)
            s.Substring(0, min s.Length MessageData<'M>.maxInfoLength)



    type MessageRecipientInfo =
        {
            recipient : MessagingClientId
            deliveryType : MessageDeliveryType
        }


    type MessageInfo<'M> =
        {
            recipientInfo : MessageRecipientInfo
            messageData : MessageData<'M>
        }


    type MessageDataInfo =
        {
            messageId : MessageId
            dataVersion : MessagingDataVersion
            sender : MessagingClientId
            recipientInfo : MessageRecipientInfo
            createdOn : DateTime
        }


        member this.isExpired (waitTime : TimeSpan) =
            match this.recipientInfo.deliveryType with
            | GuaranteedDelivery -> false
            | NonGuaranteedDelivery -> if this.createdOn.Add waitTime < DateTime.Now then true else false


    type Message<'M> =
        {
            messageDataInfo : MessageDataInfo
            messageData : MessageData<'M>
        }

        member this.isExpired waitTime = this.messageDataInfo.isExpired waitTime


    type MessageWithOptionalData<'M> =
        {
            messageDataInfo : MessageDataInfo
            messageDataOpt : MessageData<'M> option
        }
