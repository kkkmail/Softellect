namespace Softellect.Wcf

open System
open System.ServiceModel

open Softellect.Wcf.Errors
open Softellect.Sys.Core
open Softellect.Wcf.Common

module Client =

    /// ! Do not try consolidating with Servise. Different namespaces are used !
    /// This SecurityMode lives in System.ServiceModel namespace.
    type WcfSecurityMode
        with
        member s.securityMode =
            match s with
            | NoSecurity -> SecurityMode.None
            | TransportSecurity -> SecurityMode.Transport
            | MessageSecurity -> SecurityMode.Message
            | TransportWithMessageCredentialSecurity -> SecurityMode.TransportWithMessageCredential


    type ClientWcfBinding =
        | BasicHttpBinding of BasicHttpBinding
        | NetTcpBinding of NetTcpBinding


    /// There seems to be a security negotiation issue with using SecurityMode.Transport and remote WCF service.
    /// The service fails to accept connection with:
    ///     CoreWCF.Security.SecurityNegotiationException: Authentication failed on the remote side
    /// The client has a slightly different error:
    ///     System.ServiceModel.Security.SecurityNegotiationException: Authentication failed, see inner exception.
    ///     System.ComponentModel.Win32Exception (0x8009030E): No credentials are available in the security package
    /// See: https://stackoverflow.com/questions/15605688/wcf-net-tcp-with-sspi-fails-unless-client-and-server-using-same-windows-identity
    ///
    /// Gets net tcp binding suitable for sending very large data objects.
    let getNetTcpBinding (security : WcfSecurityMode) =
        let binding = new NetTcpBinding()
        binding.MaxReceivedMessageSize <- (int64 Int32.MaxValue)
        binding.MaxBufferPoolSize <- (int64 Int32.MaxValue)
        binding.MaxBufferSize <- Int32.MaxValue
        binding.OpenTimeout <- connectionTimeOut
        binding.CloseTimeout <- connectionTimeOut
        binding.SendTimeout <- dataTimeOut
        binding.ReceiveTimeout <- dataTimeOut
        binding.Security.Mode <- security.securityMode

        binding.ReaderQuotas <- getQuotas()
        binding


    /// Gets basic http binding suitable for sending very large data objects.
    let getBasicHttpBinding() =
        let binding = new BasicHttpBinding()
        binding.MaxReceivedMessageSize <- (int64 Int32.MaxValue)
        binding.MaxBufferPoolSize <- (int64 Int32.MaxValue)
        binding.MaxBufferSize <- Int32.MaxValue
        binding.OpenTimeout <- connectionTimeOut
        binding.CloseTimeout <- connectionTimeOut
        binding.SendTimeout <- dataTimeOut
        binding.ReceiveTimeout <- dataTimeOut
        binding.Security.Mode <- BasicHttpSecurityMode.None
        binding.ReaderQuotas <- getQuotas()
        binding


    let getBinding t =
        match t with
        | HttpCommunication -> getBasicHttpBinding() |> BasicHttpBinding
        | NetTcpCommunication s -> getNetTcpBinding s |> NetTcpBinding


    /// Tries getting a Wcf Client.
    let tryGetWcfService<'T> t url =
        try
            printfn $"tryGetWcfService - t: '%A{t}', url: '%A{url}'." 
            let binding = getBinding t
            let address = EndpointAddress(url)
            printfn $"tryGetWcfService - binding: '%A{binding}', address: '%A{address}'." 

            let channelFactory =
                match binding with
                | BasicHttpBinding b -> new ChannelFactory<'T>(b, address)
                | NetTcpBinding b -> new ChannelFactory<'T>(b, address)

            let service = channelFactory.CreateChannel()
            Ok (service, fun () -> channelFactory.Close())
        with
        | e -> e |> WcfExn |> Error


    /// Client communication with the server.
    let tryCommunicate t c f a =
        try
            match t() with
            | Ok (service, factoryCloser) ->
                try
                    printfn "tryCommunicate: Checking channel state..."
                    let channel = (box service) :?> IClientChannel
                    printfn $"tryCommunicate: Channel State: '%A{channel.State}', Via: '%A{channel.Via}', RemoteAddress: '%A{channel.RemoteAddress}'."

                    match trySerialize wcfSerializationFormat a with
                    | Ok b ->
                        printfn $"tryCommunicate: Calling service at %A{DateTime.Now}..." 
                        let d = c service b
                        channel.Close()
                        factoryCloser()

                        d
                        |> tryDeserialize wcfSerializationFormat
                        |> Result.mapError WcfSerializationErr
                        |> Result.mapError f
                        |> Result.bind id
                    | Error e -> toWcfSerializationError f e
                with
                | e ->
                    printfn $"tryCommunicate: At %A{DateTime.Now} got inner exception:' %A{e}'."
                    try
                        let channel = (box service) :?> IClientChannel
                        channel.Abort()
                        factoryCloser()
                    with
                    | _ -> ()

                    toWcfError f e // We want the outer "real" error.
            | Error e -> e |> f |> Error
        with
        | e ->
            printfn $"tryCommunicate: At %A{DateTime.Now} got outer exception: '%A{e}'."
            toWcfError f e
