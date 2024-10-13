namespace Softellect.Samples.DistrProc.Core

open System
open Softellect.DistributedProcessing.Primitives.Common

module Primitives =

    let solverId = "6059CA79-A97E-4DAF-B7FD-75E26ED6FB3E" |> Guid.Parse |> SolverId
    let solverName = SolverName "Test"


    /// That's 'I in the type signature.
    type TestInitialData =
        {
            x : int
        }


    /// That's 'D in the type signature.
    type TestSolverData =
        {
            x : int
        }


    /// That's 'P in the type signature.
    type TestProgressData =
        {
            x : int
        }


    /// That's 'C in the type signature.
    type TestChartData =
        {
            x : int
        }
