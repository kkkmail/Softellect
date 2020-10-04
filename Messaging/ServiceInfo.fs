﻿namespace Softellect.Messaging

open System
open System.ServiceModel

open Softellect.Sys.Errors
open Softellect.Sys.MessagingPrimitives
open Softellect.Messaging.Primitives

module ServiceInfo =

    type IMessagingService<'M, 'E> =
        abstract getVersion : unit -> StlResult<MessagingDataVersion, 'E>
        abstract sendMessage : Message<'M> -> UnitResult<'E>
        abstract tryPeekMessage : MessagingClientId -> StlResult<Message<'M> option, 'E>
        abstract tryDeleteFromServer : (MessagingClientId * MessageId) -> UnitResult<'E>


    /// https://gist.github.com/dgfitch/661656
    [<ServiceContract(ConfigurationName = MessagingWcfServiceName)>]
    type IMessagingWcfService =

        [<OperationContract(Name = "getVersion")>]
        abstract getVersion : u:byte[] -> byte[]

        [<OperationContract(Name = "sendMessage")>]
        abstract sendMessage : m:byte[] -> byte[]

        [<OperationContract(Name = "tryPeekMessage")>]
        abstract tryPeekMessage : c:byte[] -> byte[]

        [<OperationContract(Name = "tryDeleteFromServer")>]
        abstract tryDeleteFromServer : cm:byte[] -> byte[]



