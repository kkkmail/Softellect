namespace Softellect.Samples.Wcf.Client

open System
open System.Threading

open Softellect.Samples.Wcf.ServiceInfo.EchoWcfServiceInfo
open Softellect.Samples.Wcf.Client.EchoWcfClient

module Program =

    [<Literal>]
    let MaxSize = 1_000_000


    let createEchoMessage() =
        {
            x = 1
            y = DateTime.Now
            echoType = C 2
            hugeData = [ for i in 0..MaxSize -> Random().Next() ]
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