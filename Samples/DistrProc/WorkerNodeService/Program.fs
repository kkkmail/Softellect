namespace Softellect.Samples.DistrProc.WorkerNodeService

open Softellect.DistributedProcessing.WorkerNodeService.Program
open Softellect.Samples.DistrProc.ServiceInfo.Primitives
open Softellect.Samples.DistrProc.ServiceInfo.ServiceInfo
open Softellect.DistributedProcessing.Primitives.Common

module Program =

    [<EntryPoint>]
    let main args =
        let data : TestRunnerData =
            {
                workerNodeServiceInfo = workerNodeServiceInfo
                workerNodeProxy = workerNodeProxy
                messagingClientData = messagingClientData
                //tryRunSolverProcess = fun _ _ -> failwith "tryRunSolverProcess is not implemented yet."
            }

        main args
