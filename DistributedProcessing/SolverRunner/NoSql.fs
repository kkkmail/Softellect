namespace Softellect.DistributedProcessing.SolverRunner

open Softellect.DistributedProcessing.Errors
open Softellect.Sys.FileSystemTypes
open System
open Softellect.DistributedProcessing.AppSettings.SolverRunner

module NoSql =

    let solverRunnerErrTblName = TableName "SolverRunnerErr"


    let saveSolverRunnerErrFs serviceName (r : SolverRunnerCriticalError) =
        saveErrData<SolverRunnerCriticalError, Guid> getStorageFolder serviceName solverRunnerErrTblName r.errorId.value r
