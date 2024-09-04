namespace Softellect.DistributedProcessing.PartitionerService

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
open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.Errors
//open Softellect.DistributedProcessing.WorkerNodeService.DataAccess
//open Softellect.DistributedProcessing.WorkerNodeService.AppSettings
open Softellect.Sys.Primitives
open Softellect.Messaging.Client

//open ClmSys.ContGenData
//open Primitives.GeneralPrimitives
//open Softellect.Messaging.ServiceInfo
//open ClmSys.ClmErrors
//open ClmSys.ContGenPrimitives
//open ClmSys.GeneralPrimitives
//open ClmSys.WorkerNodeData
//open ClmSys.SolverData
//open ClmSys.WorkerNodePrimitives
//open ClmSys.Logging
//open Clm.ModelParams
//open Clm.CalculationData
//open ContGenServiceInfo.ServiceInfo
//open MessagingServiceInfo.ServiceInfo
//open DbData.DatabaseTypesDbo
//open DbData.DatabaseTypesClm
//open ServiceProxy.MsgProcessorProxy
//open NoSql.FileSystemTypes
//open Softellect.Sys.Primitives

module Proxy =
    let x = 1
