namespace Softellect.Samples.Messaging.MessagingServiceInfo

open System

open Softellect.Messaging.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Messaging.Client
open Softellect.Samples.Messaging.MessagingServiceInfo.EchoMsgErrors

module EchoMsgServiceInfo =

    type EchoMsgType =
        | A
        | B
        | C of int


    type EchoMessage =
        {
            messageType : EchoMsgType
            a : int
            b : DateTime
            c : List<float>
        }


    type EchoMsgData = MessageData<EchoMessage>
    type EchoMsgClient = MessagingClient<EchoMessage, EchoMsgError>
    type EchoMsgService = MessagingService<EchoMessage, EchoMsgError>
