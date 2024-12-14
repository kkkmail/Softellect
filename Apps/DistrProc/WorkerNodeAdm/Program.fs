namespace Softellect.Apps.DistrProc.WorkerNodeAdm

open Softellect.DistributedProcessing.WorkerNodeAdm.Program

module Program =

    [<EntryPoint>]
    let main argv = workerNodeAdmMain "WorkerNodeAdm" argv
