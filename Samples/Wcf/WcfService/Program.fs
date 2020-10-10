namespace Softellect.Samples.Wcf.Service

open System

open Softellect.Samples.Wcf.ServiceInfo.EchoWcfServiceInfo
open Softellect.Samples.Wcf.Service.EchoWcfService


module Program =

    [<EntryPoint>]
    let main _ =
        match echoWcfServiceDataRes with
        | Ok data ->
            match EchoWcfServiceImpl.tryGetService data with
            | Ok host ->
                let result = host.run()
                printfn "result = %A" result
            | Error e -> printfn "Error: %A" e
        | Error e -> printfn "Error: %A" e

        printfn "Press any key to exit..."
        Console.ReadLine() |> ignore
        0
