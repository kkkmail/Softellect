namespace Softellect.Samples.DistrProc.SolverRunner

open System
open Softellect.DistributedProcessing.SolverRunner.Program
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Samples.DistrProc.Core.Primitives

module Program =

    [<EntryPoint>]
    let main argv =
        let userProxy = failwith "SolverRunner is not implemented yet"
        // Call solverRunnerMain<'D, 'P, 'X, 'C>
        solverRunnerMain<TestSolverData, TestProgressData, double[], TestChartData> solverId userProxy argv
