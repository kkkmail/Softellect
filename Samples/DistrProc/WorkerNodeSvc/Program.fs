namespace Softellect.Samples.Msg.WcfWorker

open Softellect.DistributedProcessing.WorkerNodeService.Program
open Softellect.Samples.DistrProc.ServiceInfo.Primitives
open Softellect.Samples.DistrProc.ServiceInfo.ServiceInfo

module Program =

    [<EntryPoint>]
    let main args =
        let data : TestRunnereData =
            {
                workerNodeServiceInfo = workerNodeServiceInfo
                workerNodeProxy = workerNodeProxy
                messagingClientData = messagingClientData
                tryRunSolverProcess = failwith ""
            }

        main<SolverData, ProgressData> "WorkerNodeSvc" data args
