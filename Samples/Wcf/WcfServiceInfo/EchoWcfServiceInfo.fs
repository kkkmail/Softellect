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

    let serviceAddress = ServiceAddress "127.0.0.1"
    let httpServicePort = ServicePort 1080
    let httpServiceName = ServiceName "EchoHttpService"
    let netTcpServicePort = ServicePort 1088
    let netTcpServiceName = ServiceName "EchoNetTcpService"
    let httpServiceInfo = HttpServiceAccessInfo.create serviceAddress httpServicePort httpServiceName
    let netTcpServiceInfo = NetTcpServiceAccessInfo.create serviceAddress netTcpServicePort netTcpServiceName WcfSecurityMode.defaultValue
    let echoWcfServiceAccessInfo = ServiceAccessInfo.create httpServiceInfo netTcpServiceInfo
    let echoLogger = Logger.defaultValue


    type EchoServiceData =
        {
            data : int
        }

        static member create() =
            {
                data = 123
            }


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


    let echoWcfServiceDataRes =
        match WcfServiceAccessInfo.tryCreate echoWcfServiceAccessInfo with
        | Ok i ->
            {
                wcfServiceAccessInfo = i

                wcfServiceProxy =
                    {
                        wcfLogger = echoLogger
                    }

                serviceData = EchoServiceData.create()
                //setData = fun _ -> ignore()
            }
            |> Ok
        | Error e -> Error e
