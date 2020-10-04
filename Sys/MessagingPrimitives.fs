namespace Softellect.Sys

open System
open Primitives

module MessagingPrimitives =

    type MessagingDataVersion =
        | MessagingDataVersion of int

        member this.value = let (MessagingDataVersion v) = this in v


    type MessagingClientId =
        | MessagingClientId of Guid

        member this.value = let (MessagingClientId v) = this in v
        static member create() = Guid.NewGuid() |> MessagingClientId


    type MessagingClientName =
        | MessagingClientName of string

        member this.value = let (MessagingClientName v) = this in v


    type MessageId =
        | MessageId of Guid

        member this.value = let (MessageId v) = this in v
        static member create() = Guid.NewGuid() |> MessageId


    //type ServiceName
    //    with
    //    member n.messagingClientName = MessagingClientName n.originalValue


    type MessagingServiceAddress =
        | MessagingServiceAddress of ServiceAddress

        member this.value = let (MessagingServiceAddress v) = this in v
        //static member defaultValue = DefaultMessagingServiceAddress |> ServiceAddress |> MessagingServiceAddress


    type MessagingServicePort =
        | MessagingServicePort of ServicePort

        member this.value = let (MessagingServicePort v) = this in v
        //static member defaultValue = DefaultMessagingServicePort |> ServicePort |> MessagingServicePort


    type MessagingServiceName =
        | MessagingServiceName of ServiceName

        member this.value = let (MessagingServiceName v) = this in v


    let messagingServiceName = "MessagingService" |> ServiceName |> MessagingServiceName


    type VersionMismatchInfo =
        {
            localVersion : MessagingDataVersion
            remoteVersion : MessagingDataVersion
        }

