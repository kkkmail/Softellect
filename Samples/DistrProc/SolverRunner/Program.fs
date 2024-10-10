namespace Softellect.Samples.DistrProc.SolverRunner

open System
open Softellect.DistributedProcessing.SolverRunner.Program
open Softellect.DistributedProcessing.Primitives.Common

module Program =

    let solverId = "4B18CC64-CFB9-4417-93B8-16116010BBBE" |> Guid.Parse |> SolverId


    [<EntryPoint>]
    let main argv =
        // Call solverRunnerMain<'D, 'P, 'X, 'C>
        let userProxy = failwith "SolverRunner is not implemented yet"
        solverRunnerMain<unit, unit, unit, unit> solverId userProxy argv
