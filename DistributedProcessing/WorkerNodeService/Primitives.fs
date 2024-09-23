namespace Softellect.DistributedProcessing.WorkerNodeService

open System
open Softellect.Messaging.Primitives
open Softellect.Sys.Errors
open Softellect.Wcf.Common
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Client
open Softellect.Sys.Primitives
open Softellect.DistributedProcessing.Primitives

module Primitives =

    let workerNodeServiceProgramName = "WorkerNodeService.exe"


    [<Literal>]
    let WorkerNodeWcfServiceName = "WorkerNodeWcfService"


    type ServiceAccessInfo with
        static member defaultWorkerNodeValue =
            {
                netTcpServiceAddress = ServiceAddress localHost
                netTcpServicePort = defaultPartitionerNetTcpServicePort
                netTcpServiceName = WorkerNodeWcfServiceName |> ServiceName
                netTcpSecurityMode = NoSecurity
            }
            |> NetTcpServiceInfo


    let defaultWorkerNodeNetTcpServicePort = 20000 + defaultServicePort |> ServicePort
    let defaultWorkerNodeHttpServicePort = defaultWorkerNodeNetTcpServicePort.value + 1 |> ServicePort
    let defaultWorkerNodeServiceAddress = localHost |> ServiceAddress


    type WorkerNodeMessageInfo<'D> =
        {
            workerNodeRecipient : WorkerNodeId
            deliveryType : MessageDeliveryType
            messageData : WorkerNodeMessage<'D>
        }

        member this.getMessageInfo() =
            {
                recipientInfo =
                    {
                        recipient = this.workerNodeRecipient.messagingClientId
                        deliveryType = this.deliveryType
                    }
                messageData = this.messageData |> WorkerNodeMsg |> UserMsg
            }
