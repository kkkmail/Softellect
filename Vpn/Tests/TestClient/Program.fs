namespace Softellect.Vpn.TestClient

open System
open System.ServiceModel
open Softellect.Sys.Core
open Softellect.Sys.Primitives
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Core.Errors

module Program =

    [<EntryPoint>]
    let main _ =

        // Hardcoded binding for the test
        let binding = NetTcpBinding(SecurityMode.None)
        binding.MaxReceivedMessageSize <- int64 Int32.MaxValue
        binding.MaxBufferSize <- Int32.MaxValue
        binding.MaxBufferPoolSize <- int64 Int32.MaxValue

        let address = EndpointAddress("net.tcp://127.0.0.1:45001/VpnService")

        let factory = new ChannelFactory<IVpnWcfService>(binding, address)
        let proxy = factory.CreateChannel()
        let clientId = Guid("10e38c19-d220-4852-8589-82eca51ade92") |> VpnClientId

        try
            printfn "Calling authenticate..."

            let authRequest =
                {
                    clientId = clientId
                    timestamp = DateTime.Now
                    nonce = [| 1uy; 2uy; 3uy; 4uy; 5uy; 6uy; 7uy; 8uy; 9uy |]
                }

            let payload = authRequest |> serialize BinaryZippedFormat   // dummy test data
            let response = proxy.authenticate payload
            printfn $"Received response (%d{response.Length} bytes): %A{response}"

            (proxy :?> IClientChannel).Close()
            factory.Close()

            match response |> tryDeserialize<Result<VpnAuthResponse, VpnError>> BinaryZippedFormat with
            | Ok r -> printfn $"Result: '%A{r}'."
            | Error e -> printfn $"Error: '%A{e}'."
        with ex ->
            printfn "\nCALL FAILED:"
            printfn $"%s{ex.ToString()}"

            try (proxy :?> IClientChannel).Abort() with _ -> ()
            try factory.Abort() with _ -> ()

        printfn "\nDone."
        0
