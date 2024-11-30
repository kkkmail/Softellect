namespace Softellect.DistributedProcessing

open System
open Softellect.Messaging.Primitives
open Softellect.Sys.Errors
open Softellect.Wcf.Common
open Softellect.Messaging.ServiceInfo
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.Sys.Logging
open Softellect.Sys.WindowsApi
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.Errors

module Messages =

    type WorkerNodeMessage =
        | RunModelWrkMsg of (RunQueueId * SolverId * ModelBinaryData)
        | CancelRunWrkMsg of (RunQueueId * CancellationType)
        | RequestResultsWrkMsg of (RunQueueId * ResultNotificationType)
        | UpdateSolverWrkMsg of EncryptedSolver

        member this.messageSize =
            match this with
            | RunModelWrkMsg _ -> LargeSize
            | CancelRunWrkMsg _ -> SmallSize
            | RequestResultsWrkMsg _ -> SmallSize
            | UpdateSolverWrkMsg _ -> LargeSize


    type PartitionerMessage =
        | UpdateProgressPrtMsg of ProgressUpdateInfo
        | SaveResultsPrtMsg of ResultInfo
        | RegisterWorkerNodePrtMsg of WorkerNodeInfo
        | UnregisterWorkerNodePrtMsg of WorkerNodeId
        | SolverDeploymentResultMsg of WorkerNodeId * SolverId * DistributedProcessingUnitResult

        member this.messageSize =
            match this with
            | UpdateProgressPrtMsg _ -> SmallSize
            | SaveResultsPrtMsg _ -> MediumSize
            | RegisterWorkerNodePrtMsg _ -> SmallSize
            | UnregisterWorkerNodePrtMsg _ -> SmallSize
            | SolverDeploymentResultMsg _ -> SmallSize


    type DistributedProcessingMessageData =
        | PartitionerMsg of PartitionerMessage // A message sent from worker node to partitioner.
        | WorkerNodeMsg of WorkerNodeMessage // A message sent from partitioner to worker node.

        static member maxInfoLength = 500

        member this.getMessageSize() =
            match this with
            | PartitionerMsg m -> m.messageSize
            | WorkerNodeMsg m -> m.messageSize


    type DistributedProcessingMessage = Message<DistributedProcessingMessageData>
    type DistributedProcessingMessageInfo = MessageInfo<DistributedProcessingMessageData>


    type PartitionerMessageInfo =
        {
            partitionerRecipient : PartitionerId
            deliveryType : MessageDeliveryType
            messageData : PartitionerMessage
        }

        member this.getMessageInfo() =
            {
                recipientInfo =
                    {
                        recipient = this.partitionerRecipient.messagingClientId
                        deliveryType = this.deliveryType
                    }
                messageData = this.messageData |> PartitionerMsg |> UserMsg
            }


    type WorkerNodeMessageInfo =
        {
            workerNodeRecipient : WorkerNodeId
            deliveryType : MessageDeliveryType
            messageData : WorkerNodeMessage
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
