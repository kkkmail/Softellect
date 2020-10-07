namespace Softellect.Samples.Wcf.ServiceInfo

open System
open System.ServiceModel

open Softellect.Sys.Primitives
open Softellect.Sys.Logging
open Softellect.Wcf.Common
open Softellect.Wcf.Service

open Softellect.Samples.Wcf.ServiceInfo.EchoWcfErrors

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
            hugeData : list<int>
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
        }


    let echoLogger = Logger.defaultValue


    let echoWcfServiceProxy =
        {
            wcfServiceAccessInfoRes = WcfServiceAccessInfo.tryCreate echoWcfServiceAccessInfo
            loggerOpt = Some echoLogger
        }
