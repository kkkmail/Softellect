namespace Softellect.DistributedProcessing.AppSettings

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
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Core
open Softellect.Sys.AppSettings
open Softellect.Wcf.AppSettings
open Softellect.Messaging.AppSettings
//open Softellect.DistributedProcessing.WorkerNodeService.Primitives

// ==========================================

#if PARTITIONER
#endif

#if SOLVER_RUNNER || WORKER_NODE
#endif

#if SOLVER_RUNNER
#endif

#if WORKER_NODE
#endif

#if PARTITIONER || SOLVER_RUNNER || WORKER_NODE
#endif

// ==========================================

#if PARTITIONER
module PartitionerService =
#endif

#if SOLVER_RUNNER
module SolverRunner =
#endif

#if WORKER_NODE
module WorkerNodeService =
#endif

// ==========================================
#if PARTITIONER || SOLVER_RUNNER || WORKER_NODE

    let partitionerIdKey = ConfigKey "PartitionerId"


    let getPartitionerId (provider : AppSettingsProvider) d =
        match provider.tryGetGuid partitionerIdKey with
        | Ok (Some p) when p <> Guid.Empty -> p |> MessagingClientId |> PartitionerId
        | _ -> d

#endif

#if SOLVER_RUNNER || WORKER_NODE

    [<Literal>]
    let WorkerNodeWcfServiceName = "WorkerNodeWcfService"


    let workerNodeIdKey = ConfigKey "WorkerNodeId"
    let workerNodeNameKey = ConfigKey "WorkerNodeName"
    let workerNodeServiceAccessInfoKey = ConfigKey "WorkerNodeServiceAccessInfo"
    let noOfCoresKey = ConfigKey "NoOfCores"
    let isInactiveKey = ConfigKey "IsInactive"
    let nodePriorityKey = ConfigKey "NodePriority"


    type ServiceAccessInfo with
        static member defaultWorkerNodeValue =
            {
                netTcpServiceAddress = ServiceAddress localHost
                netTcpServicePort = defaultPartitionerNetTcpServicePort
                netTcpServiceName = WorkerNodeWcfServiceName |> ServiceName
                netTcpSecurityMode = NoSecurity
            }
            |> NetTcpServiceInfo


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


#endif

#if WORKER_NODE


#endif
