namespace Softellect.Communication.Samples

open System
open System.Threading

open Softellect.Communication.Samples.EchoWcfServiceInfo
open EchoWcfClient

module Program =

    let createEchoMessage() =
        {
            x = 1
            y = DateTime.Now
            echoType = C 2
        }


    let callUsingWcf() =
        let service = EchoWcfResponseHandler echoWcfServiceAccessInfo :> IEchoService

        while true do
            try
                printfn "Connecting using: %s" echoWcfServiceAccessInfo.netTcpUrl
                "Abcd" |> service.echo |> printfn "%A"
                createEchoMessage() |> service.complexEcho |> printfn "%A"
            with
            | e -> printfn "Exception: %A" e

            Thread.Sleep(2000)
        ()


    [<EntryPoint>]
    let main _ =
        do callUsingWcf()

        printfn "Hit enter to exit."
        Console.ReadLine() |> ignore
        0