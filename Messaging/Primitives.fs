namespace Softellect.Messaging

module Primitives =

    type MessageDeliveryType =
        | GuaranteedDelivery
        | NonGuaranteedDelivery


    type MessageSize =
        | SmallSize
        | MediumSize
        | LargeSize


    type MessageData<'M> =
        | TextMessage of string
        | Message of 'M

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

