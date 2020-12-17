namespace Softellect.Samples.Msg.ClientOne

open Softellect.Samples.Msg.ServiceInfo.EchoMsgServiceInfo

module Program =

    [<EntryPoint>]
    let main _ =
        runClient clientTwoData clientOneId
        0
