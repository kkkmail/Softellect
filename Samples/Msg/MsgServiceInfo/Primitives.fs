namespace Softellect.Samples.Msg.ServiceInfo

open System
open Softellect.Messaging.Primitives
open Softellect.Sys.Logging
open Softellect.Messaging.Errors

module Primitives =

    let echoDataVersion = MessagingDataVersion 2
    let echoLogger = Logger<MessagingError>.defaultValue


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
