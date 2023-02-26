namespace Softellect.Samples.Wcf.NetCoreClient

open System
open System.ServiceModel
open System.Text
open System.IO
open System.Net.Http

open Softellect.Samples.Wcf.NetCoreClient.EchoClient

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
        printfn "%s" ("http Echo(\"Hello\") => " + channel.echo("Hello"))
        do (channel :?> IClientChannel).Close()
        do factory.Close()

        let factory = new ChannelFactory<IEchoService>(new NetTcpBinding(), new EndpointAddress("net.tcp://localhost:8808/nettcp"))
        do factory.Open()
        let channel = factory.CreateChannel()
        do (channel :?> IClientChannel).Open()
        printfn "%s" ("net.tcp Echo(\"Hello\") => " + channel.echo("Hello"))
        do (channel :?> IClientChannel).Close()
        do factory.Close()

        // Complex type testing
        let factory = new ChannelFactory<IEchoService>(new BasicHttpBinding(), new EndpointAddress(basicHttpEndPointAddress))
        do factory.Open()
        let channel = factory.CreateChannel()
        do (channel :?> IClientChannel).Open()
        printfn "%s" ("http EchoMessage(\"Complex Hello\") => " + channel.complexEcho(createEchoMessage()))
        do (channel :?> IClientChannel).Close()
        do factory.Close()


    /// The following sample, creates a basic web request to the specified endpoint, sends the SOAP request and reads the response
    let callUsingWebRequest() =
        //// Does not seem to work yet.
        //// https://stackoverflow.com/questions/70185058/how-to-replace-obsolete-webclient-with-httpclient-in-net-6
        //let handler = new HttpClientHandler()
        ////handler.AutomaticDecompression <- (DecompressionMethods.GZip ||| DecompressionMethods.Deflate)

        //use httpClient = new HttpClient(handler)
        //httpClient.BaseAddress <- new Uri(basicHttpEndPointAddress)
        //let webRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(basicHttpEndPointAddress))
        //do webRequest.Headers.Add("SOAPAction", "http://tempuri.org/IEchoService/Echo")
        ////webRequest.ContentType <- "text/xml"
        ////webRequest.Method <- "POST"
        ////webRequest.ContentLength <- int64 bodyContentBytes.Length
        //webRequest.Content <- new StringContent(soapEnvelopeContent, Encoding.UTF8, "application/json")

        //let response = httpClient.Send(webRequest)
        //let reader = new StreamReader(response.Content.ReadAsStream())
        //let responseBody = reader.ReadToEnd()
        //printfn "%s" ("Http SOAP Response: " + responseBody)

        // Prepare the raw content.
        let utf8Encoder = new UTF8Encoding()
        let bodyContentBytes = utf8Encoder.GetBytes(soapEnvelopeContent)

         //Create the web request.
        let webRequest = System.Net.WebRequest.Create(new Uri(basicHttpEndPointAddress))
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
        printfn "%s" ("Http SOAP Response: " + soapResponse)
        ()


    [<EntryPoint>]
    let main _ =
        do callUsingWcf()
        do callUsingWebRequest()

        printfn "Hit enter to exit."
        Console.ReadLine() |> ignore
        0
