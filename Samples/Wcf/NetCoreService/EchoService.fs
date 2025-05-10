namespace Softellect.Samples.Wcf.NetCoreService

open CoreWCF
open System.Runtime.Serialization
open Softellect.Sys.Logging

module EchoService =

    [<DataContract>]
    type EchoMessage() =

        [<DataMember>]
        member val text = "" with get, set


    [<ServiceContract>]
    type IEchoService =

        [<OperationContract(Name = "echo")>]
        abstract echo : text:string -> string

        [<OperationContract(Name = "complexEcho")>]
        abstract complexEcho : text:EchoMessage -> string


    type EchoService() =

        interface IEchoService
            with

            member _.echo text =
                Logger.logTrace (fun () -> $"Received %s{text} from client!")
                text

            member _.complexEcho text =
                Logger.logTrace (fun () -> $"Received %s{text.text} from client!")
                text.text
