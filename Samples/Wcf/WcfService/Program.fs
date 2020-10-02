namespace Softellect.Samples.Wcf.WcfService

open System

open Softellect.Samples.Wcf.WcfServiceInfo.EchoWcfServiceInfo
open Softellect.Samples.Wcf.WcfService.EchoWcfService


module Program =

    [<EntryPoint>]
    let main _ =
        match EchoWcfServiceImpl.getService echoWcfServiceAccessInfo with
        | Ok host ->
            let result = host.run()
            printfn "result = %A" result
        | Error e -> 
            printfn "Error: %A" e
        
        printfn "Press any key to exit..."
        Console.ReadLine() |> ignore
        0
