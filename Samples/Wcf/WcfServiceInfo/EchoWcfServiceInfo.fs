namespace Softellect.Samples.Wcf.WcfServiceInfo

open System
open System.ServiceModel

open Softellect.Sys.Primitives
open Softellect.Wcf.Common

open Softellect.Samples.Wcf.WcfServiceInfo.EchoWcfErrors

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
        abstract echo : string -> UnitResult
        abstract complexEcho : EchoMessage -> EchoWcfResult<EchoReply>


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
        