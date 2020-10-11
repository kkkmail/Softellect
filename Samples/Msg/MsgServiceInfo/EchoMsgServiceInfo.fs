﻿namespace Softellect.Samples.Msg.ServiceInfo

open System
open System.Threading

open Softellect.Sys.Primitives
open Softellect.Sys.MessagingPrimitives
open Softellect.Sys.Logging
open Softellect.Sys.Errors
open Softellect.Sys.MessagingErrors
open Softellect.Wcf.Common
open Softellect.Wcf.Service
open Softellect.Messaging.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Messaging.Client
open Softellect.Messaging.Proxy

open Softellect.Samples.Msg.ServiceInfo.EchoMsgErrors

module EchoMsgServiceInfo =

    let dataVersion = MessagingDataVersion 123456
    let echoLogger = Logger.defaultValue


    type EchoMsgType =
        | A
        | B
        | C of int


    type EchoMessageData =
        {
            messageType : EchoMsgType
            a : int
            b : DateTime
            c : list<int>
        }

        static member create() =
            {
                messageType = Random().Next(100) |> C
                a = Random().Next(100)
                b = DateTime.Now
                c = [ DateTime.Now.Day; DateTime.Now.Hour; DateTime.Now.Minute; DateTime.Now.Second ]
            }


    type EchoMessagingClient = MessagingClient<EchoMessageData, EchoMsgError>
    type EchoMessagingClientData = MessagingClientData<EchoMessageData, EchoMsgError>
    type EchoMessagingServiceData = MessagingServiceData<EchoMessageData, EchoMsgError>
    type EchoMessage = Message<EchoMessageData>
    type EchoMessageInfo = MessageInfo<EchoMessageData>
    type EchoMessagingService = MessagingService<EchoMessageData, EchoMsgError>
    type EchoMessagingWcfService = MessagingWcfService<EchoMessageData, EchoMsgError>
    type EchoMessagingWcfServiceImpl = WcfService<EchoMessagingWcfService, IMessagingWcfService, EchoMessagingServiceData>


    let echoWcfServiceAccessInfo =
        {
            serviceAddress = ServiceAddress "127.0.0.1"
            httpServicePort = ServicePort 8081
            httpServiceName = ServiceName "EchoMessagingHttpService"
            netTcpServicePort =  ServicePort 8809
            netTcpServiceName = ServiceName "EchoMessagingNetTcpService"
        }


    let clientOneId = new Guid("D4CF3938-CF10-4985-9D45-DD6941092151") |> MessagingClientId
    let clientTwoId = new Guid("1AB8F97B-2F38-4947-883F-609128319C80") |> MessagingClientId
    let serviceId = new Guid("20E280F8-FAE3-49FA-933A-53BB7EABB8A9") |> MessagingClientId


    /// Using simple mutable lists to mock client and service data sources.
    type MutableList<'T> = System.Collections.Generic.List<'T>
    let private clientOneMessageData = new MutableList<EchoMessage>()
    let private clientTwoMessageData = new MutableList<EchoMessage>()
    let private serverMessageData = new MutableList<EchoMessage>()


    let private tryFind (source : MutableList<'T>) sorter finder = source |> Seq.sortBy sorter |> Seq.tryFind finder |> Ok


    let private tryDelete (source : MutableList<'T>) finder =
        printfn "tryDelete: source had %A elements." source.Count
        source.RemoveAll(fun e -> finder e) |> ignore
        printfn "tryDelete: source now has %A elements." source.Count
        Ok()


    let private isExpired (i : TimeSpan) (m : EchoMessage) =
        m.messageDataInfo.createdOn.Add(i) < DateTime.Now


    let private save (source : MutableList<'T>) finder e =
        tryDelete source finder |> ignore
        printfn "save: adding element %A to source." e
        source.Add e
        Ok()


    let private getClientProxy clientData clientId recipient : MessagingClientProxy<EchoMessageData, EchoMsgError> =
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
            toErr = fun e -> e |> MessagingClientErr |> EchoMsgErr |> SingleErr
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
            toErr = fun e -> e |> MessagingServiceErr |> EchoMsgErr |> SingleErr
        }


    let clientOneProxy = getClientProxy clientOneMessageData clientOneId clientTwoId
    let clientTwoProxy = getClientProxy clientTwoMessageData clientTwoId clientOneId


    let getClientData clientId proxy =
        {
            msgAccessInfo =
                {
                    msgClientId = clientId

                    msgSvcAccessInfo =
                        {
                            messagingServiceAccessInfo = echoWcfServiceAccessInfo
                            messagingDataVersion = dataVersion
                        }
                }

            msgClientProxy = proxy
            expirationTime = TimeSpan.FromSeconds 10.0
        }


    let clientOneData = getClientData clientOneId clientOneProxy
    let clientTwoData = getClientData clientTwoId clientTwoProxy


    let private serviceData =
        {
            messagingServiceInfo =
                {
                    expirationTime = TimeSpan.FromSeconds 10.0
                    messagingDataVersion = dataVersion
                }

            messagingServiceProxy = serviceProxy
        }


    let echoWcfServiceDataRes  =
        match WcfServiceAccessInfo.tryCreate echoWcfServiceAccessInfo with
        | Ok i ->
            {
                wcfServiceAccessInfo = i

                wcfServiceProxy =
                    {
                        wcfLogger = Logger.defaultValue
                    }

                serviceData = serviceData
                setData = fun e -> EchoMessagingService.setGetData (fun () -> Some e)
            }
            |> Ok
        | Error e -> Error e


    let runClient clientData recipient =
        let client = EchoMessagingClient clientData
        let tryProcessMessage = onTryProcessMessage client.messageProcessorProxy

        match client.start() with
        | Ok() ->
            while true do
                printfn "Sending message to %A" recipient

                let m : EchoMessageInfo =
                    {
                        recipientInfo =
                            {
                                recipient = recipient
                                deliveryType = GuaranteedDelivery
                            }

                        messageData = EchoMessageData.create() |> UserMsg
                    }

                let sendResult = client.sendMessage m
                printfn "Send with: %A" sendResult

                printfn "Checking messages."

                let checkMessage() =
                    match tryProcessMessage () (fun _ m -> m) with
                    | ProcessedSuccessfully m -> printfn "    Received message: %A" m
                    | ProcessedWithError (m, e) -> printfn "    Received message: %A with error e: %A" m e
                    | ProcessedWithFailedToRemove (m, e) -> printfn "    Received message: %A with error e: %A" m e
                    | FailedToProcess e -> printfn "    Error e: %A" e
                    | NothingToDo -> printfn "Nothing to do..."
                    | BusyProcessing -> printfn "Busy processing..."

                //let _ = [for _ in 1..20 -> ()] |> List.map checkMessage

                checkMessage()
                Thread.Sleep 5_000
        | Error e -> printfn "Error: %A" e
