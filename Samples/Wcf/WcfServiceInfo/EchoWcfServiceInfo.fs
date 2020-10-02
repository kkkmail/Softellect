namespace Softellect.Samples.Wcf.WcfServiceInfo

open System
open System.ServiceModel

open Softellect.Sys.GeneralErrors
open Softellect.Sys.Primitives
open Softellect.Wcf.Common

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


    let echoWcfServiceAccessInfo =
        {
            serviceAddress = ServiceAddress "127.0.0.1"
            httpServicePort = ServicePort 8080
            httpServiceName = ServiceName "EchoHttpService"
            netTcpServicePort =  ServicePort 8808
            netTcpServiceName = ServiceName "EchoNetTcpService"
            logError = Some (printfn "%s")
            logInfo = Some (printfn "%s")
        }
        