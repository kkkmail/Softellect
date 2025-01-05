namespace Softellect.Apps.DistrProc.PartitionerAdm

open Softellect.DistributedProcessing.PartitionerAdm.Program

module Program =

    [<EntryPoint>]
    let main argv = partitionerAdmMain "PartitionerAdm" argv
