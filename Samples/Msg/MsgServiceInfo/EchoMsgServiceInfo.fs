namespace Softellect.Samples.Msg.ServiceInfo

open System
open System.Threading

open Softellect.Sys.Primitives
open Softellect.Messaging.Primitives
open Softellect.Sys.Logging
open Softellect.Wcf.Common
open Softellect.Wcf.Service
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Messaging.Client
open Softellect.Messaging.Proxy
open Softellect.Samples.Msg.ServiceInfo.Primitives
open Softellect.Messaging.VersionInfo

module EchoMsgServiceInfo =
    type EchoMessagingClient = MessagingClient<EchoMessageData>
    type EchoMessagingClientData = MessagingClientData<EchoMessageData>
    type EchoMessagingServiceData = MessagingServiceData<EchoMessageData>
    type EchoMessage = Message<EchoMessageData>
    type EchoMessagingService = MessagingService<EchoMessageData>
    type EchoMessagingWcfService = MessagingWcfService<EchoMessageData>
    type EchoMessagingWcfServiceImpl = WcfService<EchoMessagingWcfService, IMessagingWcfService, EchoMessagingServiceData>


    let serviceAddress = ServiceAddress defaultMessagingServiceAddress
    let httpServicePort = ServicePort defaultMessagingHttpServicePort
    let httpServiceName = ServiceName "MessagingHttpService"
    let netTcpServicePort = ServicePort defaultMessagingNetTcpServicePort
    let netTcpServiceName = ServiceName "MessagingNetTcpService"

    let httpServiceInfo = HttpServiceAccessInfo.create serviceAddress httpServicePort httpServiceName
    let netTcpServiceInfo = NetTcpServiceAccessInfo.create serviceAddress netTcpServicePort netTcpServiceName WcfSecurityMode.defaultValue
    let echoMsgServiceAccessInfo = ServiceAccessInfo.create httpServiceInfo netTcpServiceInfo

    let clientOneId = Guid("D4CF3938-CF10-4985-9D45-DD6941092151") |> MessagingClientId
    let clientTwoId = Guid("1AB8F97B-2F38-4947-883F-609128319C80") |> MessagingClientId
    let serviceId = Guid("20E280F8-FAE3-49FA-933A-53BB7EABB8A9") |> MessagingClientId


    /// Using simple mutable lists to mock client and service data sources.
    type MutableList<'T> = System.Collections.Generic.List<'T>


    let private clientOneMessageData = MutableList<EchoMessage>()
    let private clientTwoMessageData = MutableList<EchoMessage>()
    let private serverMessageData = MutableList<EchoMessage>()


    let private tryFind (source : MutableList<'T>) sorter finder = source |> Seq.sortBy sorter |> Seq.tryFind finder |> Ok


    let private tryDelete (source : MutableList<'T>) finder =
        printfn $"tryDelete: source had %A{source.Count} elements."
        source.RemoveAll(fun e -> finder e) |> ignore
        printfn $"tryDelete: source now has %A{source.Count} elements."
        Ok()


    let private isExpired (i : TimeSpan) (m : EchoMessage) =
        m.messageDataInfo.createdOn.Add(i) < DateTime.Now


    let private save (source : MutableList<'T>) finder e =
        tryDelete source finder |> ignore
        printfn $"save: adding element %A{e} to source."
        source.Add e
        Ok()


    let private getClientProxy clientData clientId recipient : MessagingClientProxy<EchoMessageData> =
        {
            tryPickIncomingMessage =
                fun() ->
                    tryFind
                        clientData
                        (fun e -> e.messageDataInfo.createdOn)
                        (fun e -> e.messageDataInfo.recipientInfo.recipient = clientId)

            tryPickOutgoingMessage =
                fun() ->
                    tryFind
                        clientData
                        (fun e -> e.messageDataInfo.createdOn)
                        (fun e -> e.messageDataInfo.recipientInfo.recipient = recipient)

            saveMessage = fun m -> save clientData (fun e -> e.messageDataInfo.messageId = m.messageDataInfo.messageId) m
            tryDeleteMessage = fun i -> tryDelete clientData (fun e -> e.messageDataInfo.messageId = i)
            deleteExpiredMessages = fun i -> tryDelete clientData (isExpired i)
            getMessageSize = fun _ -> MediumSize
            logger = echoLogger
        }


    let serviceProxy =
        {
            tryPickMessage =
                fun clientId ->
                    tryFind
                        serverMessageData
                        (fun e -> e.messageDataInfo.createdOn)
                        (fun e -> e.messageDataInfo.recipientInfo.recipient = clientId)

            saveMessage =
                fun m ->
                    save serverMessageData (fun e -> e.messageDataInfo.messageId = m.messageDataInfo.messageId) m |> ignore
                    Ok()

            deleteMessage = fun i -> tryDelete serverMessageData (fun e -> e.messageDataInfo.messageId = i)
            deleteExpiredMessages = fun i -> tryDelete serverMessageData (isExpired i)
            logger = echoLogger
        }


    let clientOneProxy = getClientProxy clientOneMessageData clientOneId clientTwoId
    let clientTwoProxy = getClientProxy clientTwoMessageData clientTwoId clientOneId
    let expirationTime = TimeSpan.FromSeconds 10.0

    let createClientAccessInfo clientId = MessagingClientAccessInfo.create dataVersion echoMsgServiceAccessInfo clientId

    let getClientData clientId proxy =
        createClientAccessInfo clientId
        |> MessagingClientData.create proxy expirationTime communicationType


    let clientOneData = getClientData clientOneId clientOneProxy
    let clientTwoData = getClientData clientTwoId clientTwoProxy


    let private serviceData =
        {
            messagingServiceInfo =
                {
                    expirationTime = TimeSpan.FromSeconds 10.0
                    messagingDataVersion = dataVersion
                }

            communicationType = communicationType
            messagingServiceProxy = serviceProxy
        }


    let echoMsgServiceDataRes = tryGetMsgServiceData echoMsgServiceAccessInfo Logger.defaultValue serviceData


    let runClient clientData recipient =
        let client = EchoMessagingClient clientData
        printfn $"runClient: clientData.msgResponseHandlerData.msgAccessInfo = %A{clientData.msgResponseHandlerData.msgAccessInfo}"

        let tryProcessMessage = onTryProcessMessage client.messageProcessorProxy

        match client.start() with
        | Ok() ->
            while true do
                printfn $"Sending message to: %A{recipient}."

                let m =
                    {
                        recipientInfo =
                            {
                                recipient = recipient
                                deliveryType = GuaranteedDelivery
                            }

                        messageData = EchoMessageData.create() |> UserMsg
                    }

                let sendResult = client.sendMessage m
                printfn $"Sent with: %A{sendResult}."

                printfn "Checking messages."

                let checkMessage() =
                    match tryProcessMessage () (fun _ m -> m) with
                    | ProcessedSuccessfully m -> printfn $"    Received message: %A{m}."
                    | ProcessedWithError (m, e) -> printfn $"    Received message: %A{m} with error e: %A{e}."
                    | ProcessedWithFailedToRemove (m, e) -> printfn $"    Received message: %A{m} with error: %A{e}."
                    | FailedToProcess e -> printfn $"    Error e: %A{e}"
                    | NothingToDo -> printfn "    Nothing to do..."
                    | BusyProcessing -> printfn "    Busy processing..."

                let _ = [for _ in 1..5 -> ()] |> List.map checkMessage

                Thread.Sleep 10_000
        | Error e -> printfn $"Error: %A{e}"
