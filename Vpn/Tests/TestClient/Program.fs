namespace Softellect.Vpn.TestClient

open System
open System.ServiceModel
open System.ServiceModel.Channels
open Softellect.Sys.Core
open Softellect.Vpn.Core.ServiceInfo

module Program =

    [<EntryPoint>]
    let main _ =

        // Hardcoded binding for the test
        let binding = NetTcpBinding(SecurityMode.None)
        binding.MaxReceivedMessageSize <- int64 Int32.MaxValue
        binding.MaxBufferSize <- Int32.MaxValue
        binding.MaxBufferPoolSize <- int64 Int32.MaxValue

        // URI you expect the service to expose
        // Try VpnService first. If the service actually uses VpnWcfService, change the last part.
        let address = EndpointAddress("net.tcp://127.0.0.1:45001/VpnService")

        let factory =
            new ChannelFactory<IVpnWcfService>(binding, address)

        let proxy =
            factory.CreateChannel()

        try
            printfn "Calling authenticate..."
            let payload = [| 1uy; 2uy; 3uy; 4uy; 5uy; 6uy; 7uy; 8uy; 9uy |] |> fromByteArray |> zip   // dummy test data

            let response = proxy.authenticate payload

            printfn $"Received response (%d{response.Length} bytes): %A{response}"

            (proxy :?> IClientChannel).Close()
            factory.Close()

        with ex ->
            printfn "\nCALL FAILED:"
            printfn $"%s{ex.ToString()}"

            try (proxy :?> IClientChannel).Abort() with _ -> ()
            try factory.Abort() with _ -> ()

        printfn "\nDone."
        0
