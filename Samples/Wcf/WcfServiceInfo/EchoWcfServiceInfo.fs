namespace Softellect.Samples.Wcf.ServiceInfo

open System
open System.ServiceModel

open Softellect.Sys.Primitives
open Softellect.Sys.Logging
open Softellect.Wcf.Common
open Softellect.Wcf.Service

open Softellect.Samples.Wcf.ServiceInfo.EchoWcfErrors

module EchoWcfServiceInfo =

    let communicationType = HttpCommunication

    //let serviceAddress = ServiceAddress "127.0.0.1"
    //let httpPort = ServicePort 8080
    //httpServiceName = ServiceName "EchoHttpService"
    //let netTcpServicePort = ServicePort 8088

    let serviceAddress = ServiceAddress "13.85.24.220"
    //let serviceAddress = ServiceAddress "wcfworker20201014095213.azurewebsites.net"
    let httpServicePort = ServicePort 80
    let httpServiceName = ServiceName ""
    let netTcpServicePort = ServicePort 88


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
            serviceAddress = serviceAddress
            httpServicePort = httpServicePort
            httpServiceName = httpServiceName
            netTcpServicePort = netTcpServicePort
            netTcpServiceName = ServiceName "EchoNetTcpService"
        }


    let echoLogger = Logger.defaultValue


    let echoWcfServiceDataRes =
        match WcfServiceAccessInfo.tryCreate echoWcfServiceAccessInfo with
        | Ok i ->
            {
                wcfServiceAccessInfo = i

                wcfServiceProxy =
                    {
                        wcfLogger = echoLogger
                    }

                serviceData = ()
                setData = fun _ -> ignore()
            }
            |> Ok
        | Error e -> Error e
