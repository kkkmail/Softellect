namespace Softellect.Samples.Msg.Service

open System

open Softellect.Samples.Msg.ServiceInfo.EchoMsgServiceInfo

module Program =

    [<EntryPoint>]
    let main _ =
        match echoMsgServiceDataRes with
        | Ok data ->
            match EchoMessagingWcfServiceImpl.tryGetService data with
            | Ok host ->
                printfn "Attempting to start EchoMessagingService ..."
                let r = EchoMessagingService.tryStart()
                printfn $"result = %A{r}"

                let result = host.run()
                printfn $"result = %A{result}"

            | Error e -> printfn $"Error: %A{e}"
        | Error e -> printfn $"Error: %A{e}"

        printfn "Press any key to exit..."
        Console.ReadLine() |> ignore

        0
