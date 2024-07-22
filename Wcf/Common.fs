namespace Softellect.Wcf

open System

open Softellect.Wcf.Errors
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

        static member tryCreate s =
            match s with
            | nameof(HttpCommunication) -> Some HttpCommunication
            | nameof(NetTcpCommunication) -> Some NetTcpCommunication
            | _ -> None

        member c.value = c.ToString()


    /// Wrapper around CoreWCF.SecurityMode and System.ServiceModel.SecurityMode.
    /// Since they live in different namespaces wrapper is required to make security negotiation simpler.
    type WcfSecurityMode =
        | NoSecurity
        | TransportSecurity
        | MessageSecurity
        | TransportWithMessageCredentialSecurity

        static member defaultValue = NoSecurity


    let toValidServiceName (serviceName : string) =
        serviceName.Replace(" ", "").Replace("-", "").Replace(".", "") |> ServiceName


    let getHttpServiceUrl (ServiceAddress serviceAddress) (ServicePort servicePort) (ServiceName serviceName) =
        "http://" + serviceAddress + ":" + servicePort.ToString() + "/" + serviceName


    let getNetTcpServiceUrl (ServiceAddress serviceAddress) (ServicePort servicePort) (ServiceName serviceName) =
        "net.tcp://" + serviceAddress + ":" + (servicePort.ToString()) + "/" + serviceName


    /// https://stackoverflow.com/questions/5459697/the-maximum-message-size-quota-for-incoming-messages-65536-has-been-exceeded
    let getQuotas() =
        let readerQuotas = XmlDictionaryReaderQuotas()
        readerQuotas.MaxArrayLength <- Int32.MaxValue
        readerQuotas.MaxStringContentLength <- Int32.MaxValue
        readerQuotas.MaxDepth <- 512
        readerQuotas.MaxBytesPerRead <- Int32.MaxValue
        readerQuotas


    let toWcfError f e = e |> WcfExn |> f |> Error
    let toWcfSerializationError f e = e |> WcfSerializationErr |> f |> Error


    type HttpServiceAccessInfo =
        {
            httpServiceAddress : ServiceAddress
            httpServicePort : ServicePort
            httpServiceName : ServiceName
        }

        static member create address port name =
            {
                httpServiceAddress = address
                httpServicePort = port
                httpServiceName = name
            }


    type NetTcpServiceAccessInfo =
        {
            netTcpServiceAddress : ServiceAddress
            netTcpServicePort : ServicePort
            netTcpServiceName : ServiceName
            netTcpSecurityMode : WcfSecurityMode
        }

        static member create address port name securityMode =
            {
                netTcpServiceAddress = address
                netTcpServicePort = port
                netTcpServiceName = name
                netTcpSecurityMode = securityMode
            }


    /// Higher level (not yet parsed) service info.
    /// Note that the IP address currently should be the same but this may change.
    type ServiceAccessInfo =
        {
            httpServiceInfo : HttpServiceAccessInfo
            netTcpServiceInfo : NetTcpServiceAccessInfo
        }

        static member create httpServiceInfo netTcpServiceInfo =
            {
                httpServiceInfo = httpServiceInfo
                netTcpServiceInfo = netTcpServiceInfo
            }

        member private i.httpUrl = getHttpServiceUrl i.httpServiceInfo.httpServiceAddress i.httpServiceInfo.httpServicePort i.httpServiceInfo.httpServiceName
        member private i.netTcpUrl = getNetTcpServiceUrl i.netTcpServiceInfo.netTcpServiceAddress i.netTcpServiceInfo.netTcpServicePort i.netTcpServiceInfo.netTcpServiceName

        member i.getUrl communicationType =
            match communicationType with
            | HttpCommunication -> i.httpUrl
            | NetTcpCommunication -> i.netTcpUrl
