namespace Softellect.Sys

open System

/// Collection of messaging service related primites used in messaging related errors.
module MessagingPrimitives =

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
