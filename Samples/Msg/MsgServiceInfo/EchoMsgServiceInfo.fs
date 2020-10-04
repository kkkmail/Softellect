namespace Softellect.Samples.Msg.ServiceInfo

open System

open Softellect.Sys.Primitives
open Softellect.Sys.MessagingPrimitives
open Softellect.Wcf.Common
open Softellect.Messaging.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Messaging.Client
open Softellect.Messaging.Proxy

open Softellect.Samples.Msg.ServiceInfo.EchoMsgErrors

module EchoMsgServiceInfo =

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


    type EchoMsgClient = MessagingClient<EchoMessageData, EchoMsgError>
    type EchoMsgServiceData = MessagingServiceData<EchoMessageData, EchoMsgError>
    type EchoMsgService = MessagingService<EchoMessageData, EchoMsgError>
    type Message = Message<EchoMessageData>


    let echoWcfServiceAccessInfo =
        {
            serviceAddress = ServiceAddress "127.0.0.1"
            httpServicePort = ServicePort 8081
            httpServiceName = ServiceName "EchoMessagingHttpService"
            netTcpServicePort =  ServicePort 8809
            netTcpServiceName = ServiceName "EchoMessagingNetTcpService"
            logError = Some (printfn "%s")
            logInfo = Some (printfn "%s")
        }


    let clientOneId = new Guid("D4CF3938-CF10-4985-9D45-DD6941092151") |> MessagingClientId
    let clientTwoId = new Guid("1AB8F97B-2F38-4947-883F-609128319C80") |> MessagingClientId
    let serviceId = new Guid("20E280F8-FAE3-49FA-933A-53BB7EABB8A9") |> MessagingClientId


    /// Using simple mutable lists to mock client and service data sources.
    type MutableList<'T> = System.Collections.Generic.List<'T>
    let private clientOneData = new MutableList<Message>()
    let private clientTwoData = new MutableList<Message>()
    let private serverData = new MutableList<Message>()


    let private tryFind (source : MutableList<'T>) sorter finder = source |> Seq.sortBy sorter |> Seq.tryFind finder |> Ok


    let private tryDelete (source : MutableList<'T>) finder =
        source.RemoveAll(fun e -> finder e) |> ignore
        Ok()


    let private isExpired (i : TimeSpan) (m : Message) =
        m.messageDataInfo.createdOn.Add(i) < DateTime.Now


    let private save (source : MutableList<'T>) finder e =
        tryDelete source finder |> ignore
        source.Add e
        Ok()


    let private getClientProxy clientData clientId : MessagingClientProxy<EchoMessageData, EchoMsgError> =
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
                        (fun e -> e.messageDataInfo.recipientInfo.recipient = serviceId)

            saveMessage = fun m ->save clientData (fun e -> e.messageDataInfo.messageId = m.messageDataInfo.messageId) m
            tryDeleteMessage = fun i -> tryDelete clientData (fun e -> e.messageDataInfo.messageId = i)
            deleteExpiredMessages = fun i -> tryDelete clientData (isExpired i)
            getMessageSize = fun _ -> MediumSize
        }


    let serviceProxy : MessagingServiceProxy<EchoMessageData, EchoMsgError> =
        {
            tryPickMessage =
                fun clientId ->
                    tryFind
                        serverData
                        (fun e -> e.messageDataInfo.createdOn)
                        (fun e -> e.messageDataInfo.recipientInfo.recipient = clientId)

            saveMessage =
                fun m ->
                    save serverData (fun e -> e.messageDataInfo.messageId = m.messageDataInfo.messageId) m |> ignore
                    Ok()

            deleteMessage = fun i -> tryDelete serverData (fun e -> e.messageDataInfo.messageId = i)
            deleteExpiredMessages = fun i -> tryDelete serverData (isExpired i)
        }


    let clientOneProxy = getClientProxy clientOneData clientOneId
    let clientTwoProxy = getClientProxy clientTwoData clientTwoId


    let serviceData =
        {
            messagingServiceProxy = serviceProxy
            expirationTime = TimeSpan.FromSeconds 10.0
            messagingDataVersion  = MessagingDataVersion 0
        }
