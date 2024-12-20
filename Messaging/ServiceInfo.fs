﻿namespace Softellect.Messaging

open System
open System.ServiceModel

open Softellect.Sys.Rop
open Softellect.Sys.Primitives
open Softellect.Messaging.Primitives
open Softellect.Wcf.Common
open Softellect.Messaging.Errors
open Softellect.Messaging.VersionInfo
open Microsoft.Extensions.Hosting

module ServiceInfo =

    let defaultExpirationTime = TimeSpan.FromMinutes 5.0


    /// 'D is the strongly typed data that is being sent / received by messaging service.
    type MessagingResult<'D> = Result<'D, MessagingError>
    type MessagingOptionalResult<'D> = Result<Message<'D> option, MessagingError>
    type MessagingUnitResult = UnitResult<MessagingError>
    type MessagingStateWithResult<'D> = 'D * MessagingUnitResult


    // Client part of messaging service.
    type IMessagingClient<'D> =
        abstract getVersion : unit -> MessagingResult<MessagingDataVersion>
        abstract sendMessage : Message<'D> -> MessagingUnitResult
        abstract tryPickMessage : MessagingClientId -> MessagingOptionalResult<'D>
        abstract tryDeleteFromServer : (MessagingClientId * MessageId) -> MessagingUnitResult


    // Service part of messaging service.
    type IMessagingService<'D> =
        inherit IHostedService
        abstract getVersion : unit -> MessagingResult<MessagingDataVersion>
        abstract sendMessage : Message<'D> -> MessagingUnitResult
        abstract tryPickMessage : MessagingClientId -> MessagingOptionalResult<'D>
        abstract tryDeleteFromServer : MessagingClientId * MessageId -> MessagingUnitResult
        //abstract tryStart : unit -> MessagingUnitResult
        //abstract removeExpiredMessages : unit -> MessagingUnitResult


    /// Service WCF part of messaging service.
    /// The method removeExpiredMessages is not exposed via WCF.
    /// https://gist.github.com/dgfitch/661656
    [<ServiceContract(ConfigurationName = MessagingWcfServiceName)>]
    type IMessagingWcfService =

        [<OperationContract(Name = "getVersion")>]
        abstract getVersion : u:byte[] -> byte[]

        [<OperationContract(Name = "sendMessage")>]
        abstract sendMessage : m:byte[] -> byte[]

        [<OperationContract(Name = "tryPickMessage")>]
        abstract tryPickMessage : c:byte[] -> byte[]

        [<OperationContract(Name = "tryDeleteFromServer")>]
        abstract tryDeleteFromServer : cm:byte[] -> byte[]


    type MessagingServiceAccessInfo =
        {
            messagingDataVersion : MessagingDataVersion
            serviceAccessInfo : ServiceAccessInfo
            expirationTime : TimeSpan
        }

        static member defaultValue v =
            {
                messagingDataVersion = v
                serviceAccessInfo =
                    {
                        netTcpServiceAddress = ServiceAddress localHost
                        netTcpServicePort = getDefaultMessagingHttpServicePort v
                        netTcpServiceName = messagingServiceName.value
                        netTcpSecurityMode = NoSecurity
                    }
                    |> NetTcpServiceInfo
                expirationTime = defaultExpirationTime
            }


    type MessagingClientAccessInfo =
        {
            msgClientId : MessagingClientId
            msgSvcAccessInfo : MessagingServiceAccessInfo
        }
