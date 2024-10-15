namespace Softellect.Samples.DistrProc.SolverRunner

open System
open Softellect.DistributedProcessing.SolverRunner.Program
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.SolverRunner.OdeSolver

module Program =

    [<EntryPoint>]
    let main argv =
        let chartGenerator =
            {
                getChartData = fun _ _ _ -> { dummy = () }
                generateCharts = fun _ _ _ -> None
                generateDetailedCharts = fun _ _ _ -> []
            }

        let getUserProxy (solverData : TestSolverData) =
            let solverRunner = createOdeSolver inputParams solverData.odeParams

            let solverProxy =
                {
                    getInitialData = _.initialValues
                    getProgressData = None
                    getInvariant = fun _ _ _ -> RelativeInvariant 1.0
                }


            {
                solverRunner = solverRunner
                solverProxy = solverProxy
                chartGenerator = chartGenerator
            }

        // Call solverRunnerMain<'D, 'P, 'X, 'C>
        printfn "Calling solverRunnerMain..."
        solverRunnerMain<TestSolverData, TestProgressData, double[], TestChartData> solverId getUserProxy argv
