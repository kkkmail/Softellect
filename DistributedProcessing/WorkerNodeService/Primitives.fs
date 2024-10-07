namespace Softellect.DistributedProcessing.WorkerNodeService

open System
open Softellect.Messaging.Primitives
open Softellect.Sys.Errors
open Softellect.Wcf.Common
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Client
open Softellect.Sys.Primitives
open Softellect.DistributedProcessing.Primitives.Common

module Primitives =

    let workerNodeServiceProgramName = "WorkerNodeService.exe"


    let defaultWorkerNodeNetTcpServicePort = 20000 + defaultServicePort |> ServicePort
    let defaultWorkerNodeHttpServicePort = defaultWorkerNodeNetTcpServicePort.value + 1 |> ServicePort
    let defaultWorkerNodeServiceAddress = localHost |> ServiceAddress


    type RunQueue =
        {
            runQueueId : RunQueueId
            runQueueStatus : RunQueueStatus
            progressData : ProgressData
            createdOn : DateTime
        }


    //type WorkerNodeMessageInfo<'D> =
    //    {
    //        workerNodeRecipient : WorkerNodeId
    //        deliveryType : MessageDeliveryType
    //        messageData : WorkerNodeMessage<'D>
    //    }

    //    member this.getMessageInfo() =
    //        {
    //            recipientInfo =
    //                {
    //                    recipient = this.workerNodeRecipient.messagingClientId
    //                    deliveryType = this.deliveryType
    //                }
    //            messageData = this.messageData |> WorkerNodeMsg |> UserMsg
    //        }
