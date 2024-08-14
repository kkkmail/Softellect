namespace Softellect.Samples.Msg.WcfWorker

open Softellect.MessagingService.Program
open Softellect.Samples.Msg.ServiceInfo.Primitives
open Softellect.Samples.Msg.ServiceInfo.EchoMsgServiceInfo
open Softellect.Sys.ExitErrorCodes

module Program =

    [<EntryPoint>]
    let main args =
        match echoMsgServiceDataRes with
        | Ok r ->
            let data =
                {
                    messagingDataVersion = echoDataVersion
                    messagingServiceData = serviceData
                    wcfServiceData = r
                }

            main<EchoMessageData> "MsgWorker" data args
        | Error e ->
            printfn $"Error: '{e}'."
            CriticalError
