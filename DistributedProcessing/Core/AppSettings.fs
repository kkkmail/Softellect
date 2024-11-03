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


    type AppSettingsProvider
        with
        member p.getPartitionerId (PartitionerId d) = p.getGuidOrDefault partitionerIdKey d.value |> MessagingClientId |> PartitionerId

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
                partitionerId = provider.getPartitionerId defaultPartitionerId
                resultLocation = provider.getFolderNameOrDefault resultLocationKey FolderName.defaultResultLocation
                lastAllowedNodeErr = LastAllowedNodeErr.defaultValue
            }

        w


    let loadPartitionerServiceInfo messagingDataVersion =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let m = loadMessagingServiceAccessInfo messagingDataVersion
            let w = loadPartitionerInfo provider
            let i = getServiceAccessInfo provider partitionerServiceAccessInfoKey ServiceAccessInfo.defaultPartitionerValue

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


    type AppSettingsProvider
        with
        member p.getWorkerNodeId (WorkerNodeId d) = p.getGuidOrDefault workerNodeIdKey d.value |> MessagingClientId |> WorkerNodeId
        member p.getWorkerNodeName (WorkerNodeName d) = p.getStringOrDefault workerNodeNameKey d |> WorkerNodeName
        member p.getNoOfCores d = p.getIntOrDefault noOfCoresKey d
        member p.getNodePriority (WorkerNodePriority d) = p.getIntOrDefault nodePriorityKey d |> WorkerNodePriority
        member p.getIsInactive d = p.getBoolOrDefault isInactiveKey d


    let loadWorkerNodeInfo (provider : AppSettingsProvider) =
        let i = provider.getWorkerNodeId (WorkerNodeId.newId())
        let n = provider.getWorkerNodeName (WorkerNodeName.newName())

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
                partitionerId = provider.getPartitionerId defaultPartitionerId
                noOfCores = provider.getNoOfCores defaultNoOfCores
                nodePriority = provider.getNodePriority WorkerNodePriority.defaultValue
                isInactive = provider.getIsInactive true
                lastErrorDateOpt = None
            }

        let result = provider.trySave()
        printfn $"loadWorkerNodeInfo: result = '%A{result}'."

        w


    let loadWorkerNodeLocalInto (provider : AppSettingsProvider) =
        {
            resultLocation = provider.getFolderNameOrDefault resultLocationKey FolderName.defaultResultLocation
            solverLocation = provider.getFolderNameOrDefault solverLocationKey FolderName.defaultSolverLocation
            solverOutputLocation = provider.getFolderNameOrDefault solverOutputLocationKey FolderName.defaultSolverOutputLocation
        }


    let loadWorkerNodeServiceInfo messagingDataVersion : WorkerNodeServiceInfo =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let workerNodeSvcInfo =
                {
                    workerNodeInfo = loadWorkerNodeInfo provider
                    workerNodeLocalInto = loadWorkerNodeLocalInto provider
                    workerNodeServiceAccessInfo = getServiceAccessInfo provider workerNodeServiceAccessInfoKey ServiceAccessInfo.defaultWorkerNodeValue
                    messagingServiceAccessInfo = loadMessagingServiceAccessInfo messagingDataVersion
                }

            workerNodeSvcInfo
        | Error e -> failwith $"Cannot load settings. Error: '{e}'."

#endif
