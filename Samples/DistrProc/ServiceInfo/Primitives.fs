namespace Softellect.Samples.DistrProc.ServiceInfo

open System
open Softellect.Messaging.Primitives
open Softellect.Sys.Logging
open Softellect.Messaging.Errors
open Softellect.Sys.Primitives
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.WorkerNodeService.Proxy

module Primitives =

    let dataVersion = MessagingDataVersion 2

    type SolverDataOne =
        | SolverDataOne of int[]


    type SolverDataTwo =
        | SolverDataTwo of int[]


    type ProgressDataOne =
        | ProgressDataOne of int


    type ProgressDataTwo =
        | ProgressDataTwo of int


    type SolverData =
        | DataOne of SolverDataOne
        | DataTwo of SolverDataTwo


    type ProgressData =
        | ProgressOne of ProgressDataOne
        | ProgressTwo of ProgressDataTwo


    type TestMessageData = DistributedProcessingMessageData<SolverData, ProgressData>
    type TestRunnereData = WorkerNodeRunnerData<SolverData, ProgressData>
    type TestMessage = DistributedProcessingMessage<SolverData, ProgressData>


    let tryRunSolverProcessOne (r : RunQueueId) : DistributedProcessingUnitResult =
        failwith ""


    let tryRunSolverProcessTwo (r : RunQueueId) : DistributedProcessingUnitResult =
        failwith ""

    // ===============


    //type EchoMsgType =
    //    | A
    //    | B
    //    | C of int


    //type EchoMessageData =
    //    {
    //        messageType : EchoMsgType
    //        a : int
    //        b : DateTime
    //        c : list<int>
    //    }

    //    static member create() =
    //        {
    //            messageType = Random().Next(100) |> C
    //            a = Random().Next(100)
    //            b = DateTime.Now
    //            c = [ DateTime.Now.Day; DateTime.Now.Hour; DateTime.Now.Minute; DateTime.Now.Second ]
    //        }
