namespace Softellect.Samples.Wcf.NetCoreClient

open System.Runtime.Serialization
open System.ServiceModel


module EchoClient =

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
        