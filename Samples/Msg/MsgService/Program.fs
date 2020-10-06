namespace Softellect.Samples.Msg.Service

open System

open Softellect.Messaging.Service
open Softellect.Samples.Msg.ServiceInfo.EchoMsgServiceInfo

module Program =

    [<EntryPoint>]
    let main _ =
        let service = EchoMsgService serviceData
        //createMessagingServiceEventHandlers logger service

        printfn "Press any key to exit..."
        Console.ReadLine() |> ignore

        0
