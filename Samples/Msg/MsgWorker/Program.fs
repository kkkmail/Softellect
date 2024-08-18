namespace Softellect.Samples.Msg.WcfWorker

open Softellect.MessagingService.Program
open Softellect.Samples.Msg.ServiceInfo.Primitives
open Softellect.Samples.Msg.ServiceInfo.EchoMsgServiceInfo
open Softellect.Sys.ExitErrorCodes

module Program =

    [<EntryPoint>]
    let main args =
        let data =
            {
                messagingDataVersion = echoDataVersion
                wcfServiceData = echoMsgServiceDataRes
            }

        main<EchoMessageData> "MsgWorker" data args
