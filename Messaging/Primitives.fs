namespace Softellect.Messaging

open System
open Softellect.Sys.Primitives

/// Collection of messaging service related primites used in messaging related errors.
module Primitives =

    [<Literal>]
    let MsgDatabase = "MsgClient.db"


    [<Literal>]
    let DefaultRootFolder = @"C:\GitHub\Softellect\MessagingData\"


    [<Literal>]
    let MessagingWcfServiceName = "MessagingWcfService"


    type MessagingDataVersion =
        | MessagingDataVersion of int

        member this.value = let (MessagingDataVersion v) = this in v


    type MessageId =
        | MessageId of Guid

        member this.value = let (MessageId v) = this in v
        static member create() = Guid.NewGuid() |> MessageId


    type VersionMismatchInfo =
        {
            localVersion : MessagingDataVersion
            remoteVersion : MessagingDataVersion
        }


    type MessagingClientId =
        | MessagingClientId of Guid

        member this.value = let (MessagingClientId v) = this in v
        static member create() = Guid.NewGuid() |> MessagingClientId


    //type MessagingClientName =
    //    | MessagingClientName of string

    //    member this.value = let (MessagingClientName v) = this in v


    type MessagingServiceName =
        | MessagingServiceName of ServiceName

        member this.value = let (MessagingServiceName v) = this in v


    let messagingServiceName = "MessagingService" |> ServiceName |> MessagingServiceName
    //let messagingHttpServiceName = "MessagingHttpService" |> ServiceName |> MessagingServiceName
    //let messagingNetTcpServiceName = "MessagingNetTcpService" |> ServiceName |> MessagingServiceName


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


    type SystemMessage =
        | DataVersion of MessagingDataVersion
        | TextMessage of string


    type MessageData<'D> =
        | SystemMsg of SystemMessage
        | UserMsg of 'D

        static member maxInfoLength = 500

        member this.keepInMemory getMessageSize =
            match getMessageSize this with
            | SmallSize -> true
            | MediumSize -> false
            | LargeSize -> false

        member this.getInfo() =
            let s = $"%A{this}"
            s.Substring(0, min s.Length MessageData<'D>.maxInfoLength)


    type MessageRecipientInfo =
        {
            recipient : MessagingClientId
            deliveryType : MessageDeliveryType
        }


    type MessageInfo<'D> =
        {
            recipientInfo : MessageRecipientInfo
            messageData : MessageData<'D>
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


    type Message<'D> =
        {
            messageDataInfo : MessageDataInfo
            messageData : MessageData<'D>
        }

        member this.isExpired waitTime = this.messageDataInfo.isExpired waitTime


    type MessageWithOptionalData<'D> =
        {
            messageDataInfo : MessageDataInfo
            messageDataOpt : MessageData<'D> option
        }
