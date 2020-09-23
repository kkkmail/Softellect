namespace Softellect.Communication.Samples

open System
open System.Threading

open Softellect.Communication.Samples.EchoWcfServiceInfo
open EchoWcfClient

module Program =

    let basicHttpEndPointAddress = @"http://192.168.1.89:8080/basichttp";
    //let basicNetTcpEndPointAddress = @"net.tcp://localhost:8808/nettcp"
    let basicNetTcpEndPointAddress = @"net.tcp://192.168.1.89:8808/nettcp"


    let createEchoMessage() =
        let message = 
            {
                x = 1
                y = DateTime.Now
                echoType = C 2
            }

        message


    let callUsingWcf() =
        let address = basicNetTcpEndPointAddress
        let service = EchoWcfResponseHandler address
        //let service = EchoWcfResponseHandler basicHttpEndPointAddress

        while true do
            try
                printfn "Connecting using: %s" address
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