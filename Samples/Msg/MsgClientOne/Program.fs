namespace Softellect.Samples.Msg.ClientOne

open System
open System.Threading

open Softellect.Messaging.Client
open Softellect.Messaging.Primitives
open Softellect.Messaging.Proxy
open Softellect.Samples.Msg.ServiceInfo
open Softellect.Samples.Msg.ServiceInfo.EchoMsgServiceInfo

module Program =

    [<EntryPoint>]
    let main argv =
        let recipient = clientTwoId

        let client = EchoMsgClient clientOneData
        do client.start() |> ignore
        let tryProcessMessage = onTryProcessMessage client.messageProcessorProxy
        createMessagingClientEventHandlers client.messageProcessorProxy

        while true do
            printfn "Sending message to %A" recipient

            let m : EchoMessageInfo =
                {
                    recipientInfo =
                        {
                            recipient = recipient
                            deliveryType = GuaranteedDelivery
                        }

                    messageData = EchoMessageData.create() |> UserMsg
                }

            let sendResult = client.sendMessage m
            printfn "Send with: %A" sendResult

            printfn "Checking messages."

            let checkMessage() =
                match tryProcessMessage () (fun _ m -> m) with
                | ProcessedSuccessfully m -> printfn "    Received message: %A" m
                | ProcessedWithError (m, e) -> printfn "    Received message: %A with error e: %A" m e
                | ProcessedWithFailedToRemove (m, e) -> printfn "    Received message: %A with error e: %A" m e
                | FailedToProcess e -> printfn "    Error e: %A" e
                | NothingToDo -> printfn "Nothing to do..."
                | BusyProcessing -> printfn "Busy processing..."

            //let _ = [for _ in 1..20 -> ()] |> List.map checkMessage

            checkMessage()
            Thread.Sleep 5_000

        0
