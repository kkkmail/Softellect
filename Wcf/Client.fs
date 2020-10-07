namespace Softellect.Wcf

open System
open System.ServiceModel

open Softellect.Sys.WcfErrors
open Softellect.Sys.Core
open Softellect.Wcf.Common

module Client =

    /// Gets net tcp binding suitable for sending very large data objects.
    let getNetTcpBinding() =
        let binding = new NetTcpBinding()
        binding.MaxReceivedMessageSize <- (int64 Int32.MaxValue)
        binding.MaxBufferPoolSize <- (int64 Int32.MaxValue)
        binding.MaxBufferSize <- Int32.MaxValue
        binding.OpenTimeout <- connectionTimeOut
        binding.CloseTimeout <- connectionTimeOut
        binding.SendTimeout <- dataTimeOut
        binding.ReceiveTimeout <- dataTimeOut
        binding.Security.Mode <- SecurityMode.Transport
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


    //let getBinding() = getNetTcpBinding()
    let getBinding() = getBasicHttpBinding()


    /// Tries getting a Wcf Client.
    let tryGetWcfService<'T> url =
        try
            let binding = getBinding()
            let address = new EndpointAddress(url)
            let channelFactory = new ChannelFactory<'T>(binding, address)
            let service = channelFactory.CreateChannel()
            Ok (service, fun () -> channelFactory.Close())
        with
        | e -> e |> WcfExn |> Error


    /// Client communication with the server.
    /// Note that this is a generic with 4 implicit parameters.
    /// We can bake in the first one into t at the caller.
    /// However, to do that here requires assigning and using all 4.
    let tryCommunicate t c f a =
        try
            match t() with
            | Ok (service, factoryCloser) ->
                try
                    //printfn "tryCommunicate: Checking channel state..."
                    let channel = (box service) :?> IClientChannel
                    //printfn "tryCommunicate: Channel State: %A, Via: %A, RemoteAddress: %A." channel.State channel.Via channel.RemoteAddress

                    match trySerialize wcfSerializationFormat a with
                    | Ok b ->
                        //printfn "tryCommunicate: Calling service at %A..." DateTime.Now
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
                    try
                        let channel = (box service) :?> IClientChannel
                        channel.Abort()
                        factoryCloser()
                    with
                    | _ -> ignore()

                    toWcfError f e // We want the outer "real" error.
            | Error e -> e |> f |> Error
        with
        | e ->
            printfn "tryCommunicate: At %A got exception: %A" DateTime.Now e
            toWcfError f e
