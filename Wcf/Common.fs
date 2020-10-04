namespace Softellect.Wcf

open System
open System.ServiceModel

open Softellect.Sys.WcfErrors
open Softellect.Sys.Primitives

/// See https://stackoverflow.com/questions/53536450/merging-discriminated-unions-in-f
module Common =

    let connectionTimeOut = TimeSpan(0, 10, 0)
    let dataTimeOut = TimeSpan(1, 0, 0)
    let wcfSerializationFormat = BinaryZippedFormat
    type WcfResult<'T> = Result<'T, WcfError>


    let toValidServiceName (serviceName : string) =
        serviceName.Replace(" ", "").Replace("-", "").Replace(".", "") |> ServiceName


    let getNetTcpServiceUrl (ServiceAddress serviceAddress) (ServicePort servicePort) (ServiceName serviceName) =
        "net.tcp://" + serviceAddress + ":" + (servicePort.ToString()) + "/" + serviceName


    /// Gets net tcp binding suitable for sending very large data objects.
    let getNetTcpBinding() =
        let binding = new NetTcpBinding()
        binding.MaxReceivedMessageSize <- (int64 Int32.MaxValue)
        binding.MaxBufferSize <- Int32.MaxValue
        binding.OpenTimeout <- connectionTimeOut
        binding.CloseTimeout <- connectionTimeOut
        binding.SendTimeout <- dataTimeOut
        binding.ReceiveTimeout <- dataTimeOut
        binding.Security.Mode <- SecurityMode.Transport
        binding


    /// Gets basic http binding suitable for sending very large data objects.
    let getBasicHttpBinding() =
        let binding = new BasicHttpBinding()
        binding.MaxReceivedMessageSize <- (int64 Int32.MaxValue)
        binding.MaxBufferSize <- Int32.MaxValue
        binding.OpenTimeout <- connectionTimeOut
        binding.CloseTimeout <- connectionTimeOut
        binding.SendTimeout <- dataTimeOut
        binding.ReceiveTimeout <- dataTimeOut
        binding.Security.Mode <- BasicHttpSecurityMode.None
        binding


    let getBinding() = getNetTcpBinding()


    let toWcfError f e = e |> WcfExn |> f |> Error
    let toWcfSerializationError f e = e |> WcfSerializationErr |> f |> Error


    type ServiceAccessInfo =
        {
            serviceAddress : ServiceAddress
            httpServicePort : ServicePort
            httpServiceName : ServiceName
            netTcpServicePort : ServicePort
            netTcpServiceName : ServiceName
            logError : (string -> unit) option
            logInfo : (string -> unit) option
        }

        member i.httpUrl = "http://" + i.serviceAddress.value + ":" + i.httpServicePort.value.ToString() + "/" + i.httpServiceName.value
        member i.httpsUrl = "https://" + i.serviceAddress.value + ":" + i.httpServicePort.value.ToString() + "/" + i.httpServiceName.value
        member i.netTcpUrl = getNetTcpServiceUrl i.serviceAddress i.netTcpServicePort i.netTcpServiceName
        
