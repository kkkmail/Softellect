namespace Softellect.Samples.Msg.Service

open System

open Softellect.Messaging.Service
open Softellect.Samples.Msg.ServiceInfo
open Softellect.Samples.Msg.ServiceInfo.EchoMsgServiceInfo

module Program =

    [<EntryPoint>]
    let main _ =
        let service = EchoMsgService serviceData


        0
