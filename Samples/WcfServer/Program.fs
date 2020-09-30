namespace Softellect.Communication.Samples

open System
open Microsoft.AspNetCore.Hosting

open Softellect.Communication.Samples.EchoWcfServiceInfo
open Softellect.Communication.Samples.EchoWcfService

module Program =

    [<EntryPoint>]
    let main _ =
        match EchoWcfServiceImpl.getService echoWcfServiceAccessInfo with
        | Ok host -> host.Run()
        | Error e -> 
            printfn "Error: %A" e
            Console.ReadLine() |> ignore
        
        0
