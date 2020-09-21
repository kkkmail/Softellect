namespace Softellect.Communication.Samples

open CoreWCF
open System.Runtime.Serialization


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
        

    type EchoService =

        interface IEchoService
            with

            member _.echo text =
                printfn "Received %s from client!" text
                text

            member _.complexEcho text =
                printfn "Received %s from client!" text.text
                text.text




