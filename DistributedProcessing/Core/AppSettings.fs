namespace Softellect.DistributedProcessing

open System
open System.Threading

open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Rop
open Softellect.Sys.TimerEvents

open Softellect.Wcf.Common
open Softellect.Wcf.Client

open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy
open Softellect.Sys.Primitives
open Softellect.DistributedProcessing.Primitives
open Softellect.Sys.Core
open Softellect.Sys.AppSettings
open Softellect.Wcf.AppSettings
open Softellect.Messaging.AppSettings

module AppSettings =

    let partitionerIdKey = ConfigKey "PartitionerId"

    let workerNodeIdKey = ConfigKey "WorkerNodeId"
    let workerNodeNameKey = ConfigKey "WorkerNodeName"
    let workerNodeServiceAccessInfoKey = ConfigKey "WorkerNodeServiceAccessInfo"
    let noOfCoresKey = ConfigKey "NoOfCores"
    let isInactiveKey = ConfigKey "IsInactive"
    let nodePriorityKey = ConfigKey "NodePriority"


//    type WorkerNodeSettings =
//        {
//            workerNodeInfo : WorkerNodeInfo
//            workerNodeSvcInfo : WorkerNodeServiceAccessInfo
//            messagingSvcInfo : MessagingServiceAccessInfo
//        }

//        member w.isValid() =
//            let r =
//                [
//                    w.workerNodeInfo.workerNodeName.value <> EmptyString, $"%A{w.workerNodeInfo.workerNodeName} is invalid"
//                    w.workerNodeInfo.workerNodeId.value.value <> Guid.Empty, $"%A{w.workerNodeInfo.workerNodeId} is invalid"
//                    w.workerNodeInfo.noOfCores >= 0, $"noOfCores: %A{w.workerNodeInfo.noOfCores} is invalid"
//                    w.workerNodeInfo.partitionerId.value <> Guid.Empty, $"%A{w.workerNodeInfo.partitionerId} is invalid"

////                    w.workerNodeSvcInfo.workerNodeServiceAddress.value.value <> EmptyString, sprintf "%A is invalid" w.workerNodeSvcInfo.workerNodeServiceAddress
////                    w.workerNodeSvcInfo.workerNodeServicePort.value.value > 0, sprintf "%A is invalid" w.workerNodeSvcInfo.workerNodeServicePort
////
////                    w.messagingSvcInfo.messagingServiceAddress.value.value <> EmptyString, sprintf "%A is invalid" w.messagingSvcInfo.messagingServiceAddress
////                    w.messagingSvcInfo.messagingServicePort.value.value > 0, sprintf "%A is invalid" w.messagingSvcInfo.messagingServicePort
//                ]
//                |> List.fold(fun acc r -> combine acc r) (true, EmptyString)

//            match r with
//            | true, _ -> Ok()
//            | false, s -> s |> InvalidSettings |> Error


    let getWorkerNodeId (provider : AppSettingsProvider) d =
        match provider.tryGetGuid workerNodeIdKey with
        | Ok (Some p) when p <> Guid.Empty -> p |> MessagingClientId |> WorkerNodeId
        | _ -> d


    let getWorkerNodeName (provider : AppSettingsProvider) d =
        match provider.tryGetString workerNodeNameKey with
        | Ok (Some EmptyString) -> d
        | Ok (Some s) -> s |> WorkerNodeName
        | _ -> d


    let getNoOfCores (provider : AppSettingsProvider) d =
        match provider.tryGetInt noOfCoresKey with
        | Ok (Some k) when k >= 0 -> k
        | _ -> d


    let getNodePriority (provider : AppSettingsProvider) d =
        match provider.tryGetInt nodePriorityKey with
        | Ok (Some k) when k >= 0 -> WorkerNodePriority k
        | _ -> d


    let getIsInactive (provider : AppSettingsProvider) d =
        match provider.tryGetBool isInactiveKey with
        | Ok (Some b) -> b
        | _ -> d


    let getPartitionerId (provider : AppSettingsProvider) d =
        match provider.tryGetGuid partitionerIdKey with
        | Ok (Some p) when p <> Guid.Empty -> p |> MessagingClientId |> PartitionerId
        | _ -> d


    let loadWorkerNodeInfo (provider : AppSettingsProvider) =
        let i = getWorkerNodeId provider (WorkerNodeId.newId())
        let n = getWorkerNodeName provider (WorkerNodeName.newName())

        let defaultNoOfCores =
            match Environment.ProcessorCount with
            | 1 -> 1
            | 2 -> 1
            | 3 -> 2
            | _ -> (Environment.ProcessorCount / 2) + 1

        let w =
            {
                workerNodeId = i
                workerNodeName = n
                partitionerId = getPartitionerId provider defaultPartitionerId
                noOfCores = getNoOfCores provider defaultNoOfCores
                nodePriority = getNodePriority provider WorkerNodePriority.defaultValue
                isInactive = getIsInactive provider true
                lastErrorDateOpt = None
            }

        w


    let loadWorkerNodeServiceInfo messagingDataVersion : WorkerNodeServiceInfo =
        let providerRes = AppSettingsProvider.tryCreate AppSettingsFile

        match providerRes with
        | Ok provider ->
            let m = loadMessagingServiceAccessInfo messagingDataVersion
            let w = loadWorkerNodeInfo provider
            let i = getServiceAccessInfo providerRes workerNodeServiceAccessInfoKey ServiceAccessInfo.defaultWorkerNodeValue

            let workerNodeSvcInfo =
                {
                    workerNodeInfo = w
                    workerNodeServiceAccessInfo = i
                    messagingServiceAccessInfo = m
                }

            workerNodeSvcInfo
        | Error e -> failwith $"Cannot load settings. Error: '{e}'."


    ///// Type parameter 'P is needed because this class is shared by WorkerNodeService and WorkerNodeAdm
    ///// and they do have different type of this 'P.
    //type WorkerNodeSettingsProxy<'P> =
    //    {
    //        tryGetClientId : 'P -> WorkerNodeId option
    //        tryGetNodeName : 'P -> WorkerNodeName option
    //        tryGetPartitioner : 'P -> PartitionerId option
    //        tryGetNoOfCores : 'P -> int option
    //        tryGetInactive : 'P -> bool option
    //        tryGetServiceAddress : 'P -> ServiceAddress option
    //        tryGetServicePort : 'P -> ServicePort option
    //        tryGetMsgServiceAddress : 'P -> ServiceAddress option
    //        tryGetMsgServicePort : 'P -> ServicePort option
    //        tryGetForce : 'P -> bool option // TODO kk:20211123 - Not yet fully implemented.
    //    }
