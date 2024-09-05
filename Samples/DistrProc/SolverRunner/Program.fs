namespace Softellect.Samples.Msg.WcfWorker

open Softellect.DistributedProcessing.WorkerNodeService.Program
open Softellect.Samples.DistrProc.ServiceInfo.Primitives
open Softellect.Samples.DistrProc.ServiceInfo.ServiceInfo

module Program =
    let x = 1

    //[<EntryPoint>]
    //let main args =
    //    let data : TestRunnereData =
    //        {
    //            workerNodeServiceInfo = workerNodeServiceInfo
    //            workerNodeProxy = workerNodeProxy
    //            messagingClientData = messagingClientData
    //            tryRunSolverProcess = fun _ _ -> failwith "tryRunSolverProcess is not implemented yet."
    //        }

    //    main<SolverData, ProgressData> "WorkerNodeSvc" data args
