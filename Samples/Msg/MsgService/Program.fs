namespace Softellect.Samples.Msg.Service

open System

open Softellect.Messaging.Service
open Softellect.Samples.Msg.ServiceInfo.EchoMsgServiceInfo

//module Program =

//    [<EntryPoint>]
//    let main _ =
//        //let service = EchoMsgService serviceData
//        //service.createEventHandlers()

//        //match wcfServiceDataRes with
//        //| Ok r ->
//        //    let service = EchoMessagingWcfService r
            

//        //    failwith ""
//        //| Error e -> printfn "Error: %A" e

//        //printfn "Press any key to exit..."
//        //Console.ReadLine() |> ignore

//        match EchoMessagingWcfServiceImpl.tryGetService echoWcfServiceProxy with
//        | Ok host ->
//            let result = host.run()
//            printfn "result = %A" result
//        | Error e -> 
//            printfn "Error: %A" e
        
//        printfn "Press any key to exit..."
//        Console.ReadLine() |> ignore
//        0

module Program =

    [<EntryPoint>]
    let main _ =
        match echoWcfServiceDataRes with
        | Ok data ->
            match EchoMessagingWcfServiceImpl.tryGetService data with
            | Ok host ->
                let result = host.run()
                printfn "result = %A" result
            | Error e -> printfn "Error: %A" e
        | Error e -> printfn "Error: %A" e

        printfn "Press any key to exit..."
        Console.ReadLine() |> ignore
        0
