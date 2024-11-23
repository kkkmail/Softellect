namespace Softellect.Samples.Wcf.Client

open System
open System.Threading

open Softellect.Sys.Logging
open Softellect.Wcf.Common
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
            hugeData = [ for _ in 0..MaxSize -> Random().Next() ]
        }


    let callUsingWcf() =
        let service = EchoWcfResponseHandler echoWcfServiceAccessInfo :> IEchoService
        let url = echoWcfServiceAccessInfo.getUrl()

        while true do
            try
                Logger.logTrace $"Connecting using: %s{url}"
                let result = "Abcd" |> service.echo
                Logger.logTrace $"%A{result}"
                let result1 = createEchoMessage() |> service.complexEcho
                Logger.logTrace $"%A{result1}"
            with
            | e -> Logger.logError $"Exception: %A{e}"

            Thread.Sleep(2000)
        ()


    [<EntryPoint>]
    let main _ =
        do callUsingWcf()

        Logger.logInfo "Hit enter to exit."
        Console.ReadLine() |> ignore
        0
