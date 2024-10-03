namespace Softellect.DistributedProcessing.Primitives

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
open System
open FSharp.Data.Sql
open Softellect.Sys.Core
open Softellect.Sys.Primitives
open Softellect.Sys.Retry
open Softellect.Sys.DataAccess
open Softellect.DistributedProcessing.VersionInfo
open Softellect.Sys.AppSettings
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.Primitives
open Softellect.Sys.Errors

// ==========================================
// Blank #if template blocks

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
// Open declarations

#if PARTITIONER
#endif

#if SOLVER_RUNNER
#endif

#if WORKER_NODE
#endif

// ==========================================
// Module declarations

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
// Code
    let x = 1

#if SOLVER_RUNNER

    ///// 'P is any other data that is needed for progress tracking.
    //type ProgressData<'P> =
    //    {
    //        progressData : ProgressData
    //        progressDetailed : 'P option
    //    }
    //
    //    static member defaultValue : ProgressData<'P> =
    //        {
    //            progressData = ProgressData.defaultValue
    //            progressDetailed = None
    //        }
    //
    //    member data.estimateEndTime started = data.progressData.estimateEndTime started

#endif
