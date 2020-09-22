namespace Softellect.Communication.Samples

open System.Runtime.Serialization
open System.ServiceModel

open Softellect.Communication.Wcf
open Softellect.Core.GeneralErrors
open System


module EchoWcfServiceInfo =

    type EchoType =
        | A
        | B
        | C of int


    type EchoMessage =
        {
            x : int
            y : DateTime
            echoType : EchoType
        }


    type EchoReply =
        {
            a : int
            b : list<int>
            echoType : EchoType
        }


    [<ServiceContract>]
    type IEchoWcfService =

        [<OperationContract(Name = "echo")>]
        abstract echo : q:byte[] -> byte[]

        [<OperationContract(Name = "complexEcho")>]
        abstract complexEcho : q:byte[] -> byte[]


    type IEchoService =
        abstract echo : string -> Result<unit, WcfError>
        abstract complexEcho : EchoMessage -> Result<EchoReply, WcfError>
        