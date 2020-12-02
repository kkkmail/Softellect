namespace Softellect.Wcf

open System

open Softellect.Sys.WcfErrors
open Softellect.Sys.Primitives
open Softellect.Sys.Logging
open System.Xml

/// See https://stackoverflow.com/questions/53536450/merging-discriminated-unions-in-f
module Common =

    let connectionTimeOut = TimeSpan(0, 10, 0)
    let dataTimeOut = TimeSpan(1, 0, 0)
    let wcfSerializationFormat = BinaryZippedFormat
    type WcfResult<'T> = Result<'T, WcfError>
    type WcfLogger = Logger<WcfError>


    type WcfCommunicationType =
        | HttpCommunication
        | NetTcpCommunication


    let toValidServiceName (serviceName : string) =
        serviceName.Replace(" ", "").Replace("-", "").Replace(".", "") |> ServiceName


    let getNetTcpServiceUrl (ServiceAddress serviceAddress) (ServicePort servicePort) (ServiceName serviceName) =
        "net.tcp://" + serviceAddress + ":" + (servicePort.ToString()) + "/" + serviceName


    /// https://stackoverflow.com/questions/5459697/the-maximum-message-size-quota-for-incoming-messages-65536-has-been-exceeded
    let getQuotas() =
        let readerQuotas = new XmlDictionaryReaderQuotas()
        readerQuotas.MaxArrayLength <- Int32.MaxValue
        readerQuotas.MaxStringContentLength <- Int32.MaxValue
        readerQuotas.MaxDepth <- 512
        readerQuotas.MaxBytesPerRead <- Int32.MaxValue
        readerQuotas


    let toWcfError f e = e |> WcfExn |> f |> Error
    let toWcfSerializationError f e = e |> WcfSerializationErr |> f |> Error


    /// Higher level (not yet parsed) service info.
    type ServiceAccessInfo =
        {
            serviceAddress : ServiceAddress
            httpServicePort : ServicePort
            httpServiceName : ServiceName
            netTcpServicePort : ServicePort
            netTcpServiceName : ServiceName
        }

        member i.httpUrl = "http://" + i.serviceAddress.value + ":" + i.httpServicePort.value.ToString() + "/" + i.httpServiceName.value
        member i.httpsUrl = "https://" + i.serviceAddress.value + ":" + i.httpServicePort.value.ToString() + "/" + i.httpServiceName.value
        member i.netTcpUrl = getNetTcpServiceUrl i.serviceAddress i.netTcpServicePort i.netTcpServiceName
