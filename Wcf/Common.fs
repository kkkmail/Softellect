namespace Softellect.Wcf

open System

open Softellect.Wcf.Errors
open Softellect.Sys.Primitives
open Softellect.Sys.Logging
open Softellect.Sys.Core
open Softellect.Sys.AppSettings
open System.Xml


/// See https://stackoverflow.com/questions/53536450/merging-discriminated-unions-in-f
module Common =

    let connectionTimeOut = TimeSpan(0, 10, 0)
    let dataTimeOut = TimeSpan(1, 0, 0)
    let wcfSerializationFormat = BinaryZippedFormat


    type WcfResult<'T> = Result<'T, WcfError>


    /// Wrapper around CoreWCF.SecurityMode and System.ServiceModel.SecurityMode.
    /// Since they live in different namespaces wrapper is required to make security negotiation simpler.
    type WcfSecurityMode =
        | NoSecurity
        | TransportSecurity
        | MessageSecurity
        | TransportWithMessageCredentialSecurity

        static member defaultValue = NoSecurity
        member a.serialize() = $"{a}"

        static member tryDeserialize s =
            match s with
            | nameof(NoSecurity) -> Some NoSecurity
            | nameof(TransportSecurity) -> Some TransportSecurity
            | nameof(MessageSecurity) -> Some MessageSecurity
            | nameof(TransportWithMessageCredentialSecurity) -> Some TransportWithMessageCredentialSecurity
            | _ -> None


    type WcfCommunicationType =
        | HttpCommunication
        | NetTcpCommunication of WcfSecurityMode

        member c.serialize() = jsonSerialize c
            //serialize
            //match c with
            //| HttpCommunication -> nameof(HttpCommunication)
            //| NetTcpCommunication s -> $"{nameof(NetTcpCommunication)}:{s}"

        static member tryCreate s = 
            try
                jsonDeserialize<WcfCommunicationType> s |> Some
            with
            | e ->
                printfn $"tryCreate: Exception: '%A{e}'."
                None

            //match s with
            //| nameof(HttpCommunication) -> Some HttpCommunication
            //| nameof(NetTcpCommunication) -> Some NetTcpCommunication
            //| _ -> None

        member c.value = c.ToString()


    let toValidServiceName (serviceName : string) =
        serviceName.Replace(" ", "").Replace("-", "").Replace(".", "") |> ServiceName


    let getHttpServiceUrl (ServiceAddress serviceAddress) (ServicePort servicePort) (ServiceName serviceName) =
        "http://" + serviceAddress.value + ":" + servicePort.ToString() + "/" + serviceName


    let getNetTcpServiceUrl (ServiceAddress serviceAddress) (ServicePort servicePort) (ServiceName serviceName) =
        "net.tcp://" + serviceAddress.value + ":" + (servicePort.ToString()) + "/" + serviceName


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


    let tryDeserializeServiceAddress p v = p |> Map.tryFind v |> Option.map ServiceAddress.tryDeserialize |> Option.flatten
    let tryDeserializeServicePort p v = p |> Map.tryFind v |> Option.map ServicePort.tryDeserialize |> Option.flatten
    let tryDeserializeServiceName p v = p |> Map.tryFind v |> Option.map ServiceName.tryDeserialize |> Option.flatten
    let tryDeserializeSecurityMode p v = p |> Map.tryFind v |> Option.map WcfSecurityMode.tryDeserialize |> Option.flatten


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

        static member private defaultValue = HttpServiceAccessInfo.create (ServiceAddress localHost) (ServicePort 0) (ServiceName String.Empty)

        member i.serialize() =
            $"{nameof(i.httpServiceAddress)}{ValueSeparator}{i.httpServiceAddress.serialize()}{ListSeparator}" +
            $"{nameof(i.httpServicePort)}{ValueSeparator}{i.httpServicePort.serialize()}{ListSeparator}" +
            $"{nameof(i.httpServiceName)}{ValueSeparator}{i.httpServiceName.serialize()}"

        static member tryDeserialize (s : string) =
            let d = HttpServiceAccessInfo.defaultValue
            let p = parseSimpleSetting s

            match nameof(d.httpServiceAddress) |> tryDeserializeServiceAddress p, nameof(d.httpServicePort) |> tryDeserializeServicePort p, nameof(d.httpServiceName) |> tryDeserializeServiceName p with
            | Some a, Some b, Some c -> HttpServiceAccessInfo.create a b c |> Some
            | _ ->
                printfn $"HttpServiceAccessInfo.tryDeserialize - Invalid input: '{s}'."
                None


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

        static member private defaultValue = NetTcpServiceAccessInfo.create (ServiceAddress localHost) (ServicePort 0) (ServiceName String.Empty) NoSecurity

        member i.serialize() =
            $"{nameof(i.netTcpServiceAddress)}{ValueSeparator}{i.netTcpServiceAddress.serialize()}{ListSeparator}" +
            $"{nameof(i.netTcpServicePort)}{ValueSeparator}{i.netTcpServicePort.serialize()}{ListSeparator}" +
            $"{nameof(i.netTcpServiceName)}{ValueSeparator}{i.netTcpServiceName.serialize()}{ListSeparator}" +
            $"{nameof(i.netTcpSecurityMode)}{ValueSeparator}{i.netTcpSecurityMode.serialize()}"

        static member tryDeserialize (s : string) =
            let d = NetTcpServiceAccessInfo.defaultValue
            let p = parseSimpleSetting s

            match nameof(d.netTcpServiceAddress) |> tryDeserializeServiceAddress p, nameof(d.netTcpServicePort) |> tryDeserializeServicePort p, nameof(d.netTcpServiceName) |> tryDeserializeServiceName p, nameof(d.netTcpSecurityMode) |> tryDeserializeSecurityMode p with
            | Some a, Some b, Some c, Some d -> NetTcpServiceAccessInfo.create a b c d |> Some
            | _ ->
                printfn $"NetTcpServiceAccessInfo.tryDeserialize - Invalid input: '{s}'."
                None


    type ServiceAccessInfo =
        | HttpServiceInfo of HttpServiceAccessInfo
        | NetTcpServiceInfo of NetTcpServiceAccessInfo

        member i.getUrl() =
            match i with
            | HttpServiceInfo n ->  getHttpServiceUrl n.httpServiceAddress n.httpServicePort n.httpServiceName
            | NetTcpServiceInfo n -> getNetTcpServiceUrl n.netTcpServiceAddress n.netTcpServicePort n.netTcpServiceName

        member i.communicationType =
            match i with
            | HttpServiceInfo _ -> HttpCommunication
            | NetTcpServiceInfo n -> NetTcpCommunication n.netTcpSecurityMode


        member i.serialize() =
            match i with
            | HttpServiceInfo n -> $"{nameof(HttpCommunication)}{DiscriminatedUnionSeparator}{n.serialize()}"
            | NetTcpServiceInfo n -> $"{nameof(NetTcpServiceInfo)}{DiscriminatedUnionSeparator}{n.serialize()}"


        static member tryDeserialize (s : string) =
            let startsWithHttp = $"{nameof(HttpServiceInfo)}{DiscriminatedUnionSeparator}"
            let startsWithNetTcp = $"{nameof(NetTcpServiceInfo)}{DiscriminatedUnionSeparator}"

            let s1 = s.Replace(" ", "")

            match s1.StartsWith startsWithHttp with
            | true ->
                let r = s1.Substring(startsWithHttp.Length) |> HttpServiceAccessInfo.tryDeserialize |> Option.bind (fun e -> e |> HttpServiceInfo |> Some)

                match r with
                | Some v -> Ok v
                | None -> Error s
            | false ->
                match s1.StartsWith startsWithNetTcp with
                | true ->
                    let r = s1.Substring(startsWithNetTcp.Length) |> NetTcpServiceAccessInfo.tryDeserialize |> Option.bind (fun e -> e |> NetTcpServiceInfo |> Some)

                    match r with
                    | Some v -> Ok v
                    | None -> Error s
                | false -> Error s
