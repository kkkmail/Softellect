﻿namespace Softellect.Communication.Samples

open System.Runtime.Serialization
open System.ServiceModel


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
        