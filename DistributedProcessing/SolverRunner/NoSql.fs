namespace Softellect.DistributedProcessing.SolverRunner

open Softellect.DistributedProcessing.Errors
open Softellect.Sys.FileSystemTypes
open System

module NoSql =

    let solverRunnerErrTblName = TableName "SolverRunnerErr"


    let saveSolverRunnerErrFs serviceName (r : SolverRunnerCriticalError) =
        saveErrData<SolverRunnerCriticalError, Guid> serviceName solverRunnerErrTblName r.errorId.value r
