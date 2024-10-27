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
// Blank #if template blocks

#if PARTITIONER
#endif

#if MODEL_GENERATOR
#endif

#if PARTITIONER || MODEL_GENERATOR
#endif

#if SOLVER_RUNNER || WORKER_NODE
#endif

#if SOLVER_RUNNER
#endif

#if WORKER_NODE
#endif

#if PARTITIONER || PARTITIONER_ADM || MODEL_GENERATOR || SOLVER_RUNNER || WORKER_NODE
#endif

// ==========================================
// Module declarations

#if PARTITIONER
module PartitionerService =
#endif

#if PARTITIONER_ADM
module PartitionerAdm =
#endif

#if MODEL_GENERATOR
module ModelGenerator =
#endif

#if SOLVER_RUNNER
module SolverRunner =
#endif

#if WORKER_NODE
module WorkerNodeService =
#endif

// ==========================================
// Code

#if PARTITIONER || PARTITIONER_ADM || MODEL_GENERATOR || SOLVER_RUNNER || WORKER_NODE

    let partitionerIdKey = ConfigKey "PartitionerId"
    let resultLocationKey = ConfigKey "ResultLocation"
    let solverLocationKey = ConfigKey "SolverLocation"
    let solverOutputLocationKey = ConfigKey "SolverOutputLocation"


    let getPartitionerId (provider : AppSettingsProvider) d =
        match provider.tryGetGuid partitionerIdKey with
        | Ok (Some p) when p <> Guid.Empty -> p |> MessagingClientId |> PartitionerId
        | _ -> d


    let tryGetFolderName (provider : AppSettingsProvider) key =
        match provider.tryGetString key with
        | Ok (Some p) when p <> "" ->
            match FolderName.tryCreate p with
            | Ok f -> Some f
            | Error _ -> None
        | _ -> None


    let getFolderName (provider : AppSettingsProvider) key d =
        match provider.tryGetString key with
        | Ok (Some p) ->
            match FolderName.tryCreate p with
            | Ok f -> f
            | Error _ -> d
        | _ -> d

#endif

#if PARTITIONER || PARTITIONER_ADM || MODEL_GENERATOR

    [<Literal>]
    let PartitionerWcfServiceName = "PartitionerWcfService"


    let partitionerServiceAccessInfoKey = ConfigKey "PartitionerServiceAccessInfo"


    type ServiceAccessInfo with
        static member defaultPartitionerValue =
            {
                netTcpServiceAddress = ServiceAddress localHost
                netTcpServicePort = defaultPartitionerNetTcpServicePort
                netTcpServiceName = PartitionerWcfServiceName |> ServiceName
                netTcpSecurityMode = NoSecurity
            }
            |> NetTcpServiceInfo


    let loadPartitionerInfo (provider : AppSettingsProvider) =
        let w =
            {
                partitionerId = getPartitionerId provider defaultPartitionerId
                resultLocation = getFolderName provider resultLocationKey FolderName.defaultResultLocation
                lastAllowedNodeErr = LastAllowedNodeErr.defaultValue
            }

        w


    let loadPartitionerServiceInfo messagingDataVersion =
        let providerRes = AppSettingsProvider.tryCreate AppSettingsFile

        match providerRes with
        | Ok provider ->
            let m = loadMessagingServiceAccessInfo messagingDataVersion
            let w = loadPartitionerInfo provider
            let i = getServiceAccessInfo providerRes partitionerServiceAccessInfoKey ServiceAccessInfo.defaultPartitionerValue

            let partitionerSvcInfo =
                {
                    partitionerInfo = w
                    partitionerServiceAccessInfo = i
                    messagingServiceAccessInfo = m
                }

            partitionerSvcInfo
        | Error e -> failwith $"Cannot load settings. Error: '{e}'."

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


    let loadWorkerNodeLocalInto provider =
        {
            resultLocation = getFolderName provider resultLocationKey FolderName.defaultResultLocation
            solverLocation = getFolderName provider solverLocationKey FolderName.defaultSolverLocation
            solverOutputLocation = tryGetFolderName provider solverOutputLocationKey
        }


    let loadWorkerNodeServiceInfo messagingDataVersion : WorkerNodeServiceInfo =
        let providerRes = AppSettingsProvider.tryCreate AppSettingsFile

        match providerRes with
        | Ok provider ->
            let workerNodeSvcInfo =
                {
                    workerNodeInfo = loadWorkerNodeInfo provider
                    workerNodeLocalInto = loadWorkerNodeLocalInto provider
                    workerNodeServiceAccessInfo = getServiceAccessInfo providerRes workerNodeServiceAccessInfoKey ServiceAccessInfo.defaultWorkerNodeValue
                    messagingServiceAccessInfo = loadMessagingServiceAccessInfo messagingDataVersion
                }

            workerNodeSvcInfo
        | Error e -> failwith $"Cannot load settings. Error: '{e}'."

#endif
