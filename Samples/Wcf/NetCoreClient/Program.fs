namespace Softellect.Samples.Wcf.NetCoreClient

open System
open System.ServiceModel
open System.Text
open System.IO
open System.Net.Http

open Softellect.Samples.Wcf.NetCoreClient.EchoClient
open Softellect.Sys.Logging

/// Port of C# CoreWCF client example.
module Program =

    let basicHttpEndPointAddress = @"http://localhost:8080/basichttp";
    let soapEnvelopeContent = "<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\"><soapenv:Body><Echo xmlns = \"http://tempuri.org/\" ><text>Hello</text></Echo></soapenv:Body></soapenv:Envelope>";


    let createEchoMessage() =
        let message = new EchoMessage()
        message.text <- "Complex Hello"
        message


    let callUsingWcf() =
        let factory = new ChannelFactory<IEchoService>(new BasicHttpBinding(), new EndpointAddress(basicHttpEndPointAddress))
        do factory.Open()
        let channel = factory.CreateChannel()
        do (channel :?> IClientChannel).Open()
        Logger.logTrace (fun () -> $"""http Echo("Hello") => {channel.echo "Hello"}""")
        do (channel :?> IClientChannel).Close()
        do factory.Close()

        let factory = new ChannelFactory<IEchoService>(new NetTcpBinding(), new EndpointAddress("net.tcp://localhost:8808/nettcp"))
        do factory.Open()
        let channel = factory.CreateChannel()
        do (channel :?> IClientChannel).Open()
        Logger.logTrace (fun () -> $"""net.tcp Echo("Hello") => {channel.echo "Hello"}""")
        do (channel :?> IClientChannel).Close()
        do factory.Close()

        // Complex type testing
        let factory = new ChannelFactory<IEchoService>(new BasicHttpBinding(), new EndpointAddress(basicHttpEndPointAddress))
        do factory.Open()
        let channel = factory.CreateChannel()
        do (channel :?> IClientChannel).Open()
        Logger.logTrace (fun() -> $"""http EchoMessage("Complex Hello") => {channel.complexEcho (createEchoMessage())}""")
        do (channel :?> IClientChannel).Close()
        do factory.Close()


    /// The following sample creates a basic web request to the specified endpoint, sends the SOAP request and reads the response
    let callUsingWebRequest() =
        // Prepare the raw content.
        let utf8Encoder = new UTF8Encoding()
        let bodyContentBytes = utf8Encoder.GetBytes(soapEnvelopeContent)

         //Create the web request.
        let webRequest = System.Net.WebRequest.Create(Uri(basicHttpEndPointAddress))
        do webRequest.Headers.Add("SOAPAction", "http://tempuri.org/IEchoService/Echo")
        webRequest.ContentType <- "text/xml"
        webRequest.Method <- "POST"
        webRequest.ContentLength <- int64 bodyContentBytes.Length

        // Append the content.
        let requestContentStream = webRequest.GetRequestStream()
        do requestContentStream.Write(bodyContentBytes, 0, bodyContentBytes.Length)

        // Send the request and read the response.
        use responseStream = webRequest.GetResponse().GetResponseStream()
        use responseReader = new StreamReader(responseStream)
        let soapResponse = responseReader.ReadToEnd()
        Logger.logTrace (fun () -> $"""Http SOAP Response: {soapResponse}""")
        ()


    [<EntryPoint>]
    let main _ =
        do callUsingWcf()
        do callUsingWebRequest()

        Logger.logInfo "Hit enter to exit."
        Console.ReadLine() |> ignore
        0
