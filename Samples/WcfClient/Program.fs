namespace Softellect.Communication.Samples

open System
open System.Threading

open Softellect.Communication.Samples.EchoWcfServiceInfo
open EchoWcfClient

module Program =

    let basicNetTcpEndPointAddress = @"net.tcp://localhost:8808/nettcp"


    let createEchoMessage() =
        let message = 
            {
                x = 1
                y = DateTime.Now
                echoType = C 2
            }

        message


    let callUsingWcf() =
        let service = EchoWcfResponseHandler basicNetTcpEndPointAddress

        while true do
            try
                "Abcd" |> (service :> IEchoService).echo |> printfn "%A"
                createEchoMessage() |> (service :> IEchoService).complexEcho |> printfn "%A"
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