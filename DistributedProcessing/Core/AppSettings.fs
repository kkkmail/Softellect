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

module AppSettings =

    let partitionerIdKey = ConfigKey "PartitionerId"

    let workerNodeIdKey = ConfigKey "WorkerNodeId"
    let workerNodeNameKey = ConfigKey "WorkerNodeName"
    let workerNodeServiceAddressKey = ConfigKey "WorkerNodeServiceAddress"
    let workerNodeServiceHttpPortKey = ConfigKey "WorkerNodeServiceHttpPort"
    let workerNodeServiceNetTcpPortKey = ConfigKey "WorkerNodeServiceNetTcpPort"
    let workerNodeServiceCommunicationTypeKey = ConfigKey "WorkerNodeServiceCommunicationType"

    let noOfCoresKey = ConfigKey "NoOfCores"
    let isInactiveKey = ConfigKey "IsInactive"
    let nodePriorityKey = ConfigKey "NodePriority"


    type WorkerNodeSettings =
        {
            workerNodeInfo : WorkerNodeInfo
            workerNodeSvcInfo : WorkerNodeServiceAccessInfo
            workerNodeCommunicationType : WcfCommunicationType
            messagingSvcInfo : MessagingServiceAccessInfo
            messagingCommunicationType : WcfCommunicationType
        }

        member w.isValid() =
            let r =
                [
                    w.workerNodeInfo.workerNodeName.value <> EmptyString, $"%A{w.workerNodeInfo.workerNodeName} is invalid"
                    w.workerNodeInfo.workerNodeId.value.value <> Guid.Empty, $"%A{w.workerNodeInfo.workerNodeId} is invalid"
                    w.workerNodeInfo.noOfCores >= 0, $"noOfCores: %A{w.workerNodeInfo.noOfCores} is invalid"
                    w.workerNodeInfo.partitionerId.value <> Guid.Empty, $"%A{w.workerNodeInfo.partitionerId} is invalid"

//                    w.workerNodeSvcInfo.workerNodeServiceAddress.value.value <> EmptyString, sprintf "%A is invalid" w.workerNodeSvcInfo.workerNodeServiceAddress
//                    w.workerNodeSvcInfo.workerNodeServicePort.value.value > 0, sprintf "%A is invalid" w.workerNodeSvcInfo.workerNodeServicePort
//
//                    w.messagingSvcInfo.messagingServiceAddress.value.value <> EmptyString, sprintf "%A is invalid" w.messagingSvcInfo.messagingServiceAddress
//                    w.messagingSvcInfo.messagingServicePort.value.value > 0, sprintf "%A is invalid" w.messagingSvcInfo.messagingServicePort
                ]
                |> List.fold(fun acc r -> combine acc r) (true, EmptyString)

            match r with
            | true, _ -> Ok()
            | false, s -> s |> InvalidSettings |> Error


    let loadWorkerNodeServiceSettings providerRes =
        let workerNodeServiceAddress = getServiceAddress providerRes workerNodeServiceAddressKey defaultWorkerNodeServiceAddress
        let workerNodeServiceHttpPort = getServiceHttpPort providerRes workerNodeServiceHttpPortKey defaultWorkerNodeHttpServicePort
        let workerNodeServiceNetTcpPort = getServiceNetTcpPort providerRes workerNodeServiceNetTcpPortKey defaultWorkerNodeNetTcpServicePort
        let workerNodeServiceCommunicationType = getCommunicationType providerRes workerNodeServiceCommunicationTypeKey (NetTcpCommunication NoSecurity)

        let workerNodeSvcInfo =
            WorkerNodeServiceAccessInfo.create workerNodeServiceAddress workerNodeServiceHttpPort workerNodeServiceNetTcpPort WcfSecurityMode.defaultValue

        (workerNodeSvcInfo, workerNodeServiceCommunicationType)


    let tryGetWorkerNodeId (providerRes : AppSettingsProviderResult) =
        match providerRes with
        | Ok provider ->
            match provider.tryGetGuid workerNodeIdKey with
            | Ok (Some p) when p <> Guid.Empty -> p |> MessagingClientId |> WorkerNodeId |> Some
            | _ -> None
        | _ -> None


    let tryGetWorkerNodeName (providerRes : AppSettingsProviderResult) =
        match providerRes with
        | Ok provider ->
            match provider.tryGetString workerNodeNameKey with
            | Ok (Some EmptyString) -> None
            | Ok (Some s) -> s |> WorkerNodeName |> Some
            | _ -> None
        | _ -> None


    let getNoOfCores (providerRes : AppSettingsProviderResult) d =
        match providerRes with
        | Ok provider ->
            match provider.tryGetInt noOfCoresKey with
            | Ok (Some k) when k >= 0 -> k
            | _ -> d
        | _ -> d


    let getNodePriority (providerRes : AppSettingsProviderResult) d =
        match providerRes with
        | Ok provider ->
            match provider.tryGetInt nodePriorityKey with
            | Ok (Some k) when k >= 0 -> k
            | _ -> d
        | _ -> d
        |> WorkerNodePriority


    let getIsInactive (providerRes : AppSettingsProviderResult) d =
        match providerRes with
        | Ok provider ->
            match provider.tryGetBool isInactiveKey with
            | Ok (Some b) -> b
            | _ -> d
        | _ -> d


    let getPartitionerId (providerRes : AppSettingsProviderResult) d =
        match providerRes with
        | Ok provider ->
            match provider.tryGetGuid partitionerIdKey with
            | Ok (Some p) when p <> Guid.Empty -> p |> MessagingClientId |> PartitionerId
            | _ -> d
        | _ -> d


    let tryLoadWorkerNodeInfo (providerRes : AppSettingsProviderResult) nodeIdOpt nameOpt =
        let io = nodeIdOpt |> Option.orElseWith (fun () -> tryGetWorkerNodeId providerRes)
        let no = nameOpt |> Option.orElseWith (fun () -> tryGetWorkerNodeName providerRes)

        match io, no with
        | Some i, Some n ->
            let defaultNoOfCores =
                match Environment.ProcessorCount with
                | 1 -> 1
                | 2 -> 1
                | 3 -> 2
                | _ -> (Environment.ProcessorCount / 2) + 1

            let w =
                {
                    workerNodeId = i
                    workerNodeName  = n
                    partitionerId = getPartitionerId providerRes defaultPartitionerId
                    noOfCores = getNoOfCores providerRes defaultNoOfCores
                    nodePriority = getNodePriority providerRes WorkerNodePriority.defaultValue.value
                    isInactive = getIsInactive providerRes true
                    lastErrorDateOpt = None
                }

            Some w
        | _ -> None


    /// Type parameter 'P is needed because this class is shared by WorkerNodeService and WorkerNodeAdm
    /// and they do have different type of this 'P.
    type WorkerNodeSettingsProxy<'P> =
        {
            tryGetClientId : 'P -> WorkerNodeId option
            tryGetNodeName : 'P -> WorkerNodeName option
            tryGetPartitioner : 'P -> PartitionerId option
            tryGetNoOfCores : 'P -> int option
            tryGetInactive : 'P -> bool option
            tryGetServiceAddress : 'P -> ServiceAddress option
            tryGetServicePort : 'P -> ServicePort option
            tryGetMsgServiceAddress : 'P -> ServiceAddress option
            tryGetMsgServicePort : 'P -> ServicePort option
            tryGetForce : 'P -> bool option // TODO kk:20211123 - Not yet fully implemented.
        }
