namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Errors
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.Messaging.Primitives
open Softellect.Sys.Rop

/// A SolverRunner operates on input data 'D and produces progress / output data 'P.
/// Internally it uses a data type 'X to store the state of the computation and may produce chart data 'C.
/// So, 4 generics are used: 'D, 'P, 'X, 'C and it is a minimum that's needed.
/// "Charts" are some slices of data that are produced during the computation and can be used to visualize the progress.
/// We do it that way because both 'D and 'X could be huge and so capturing the state of the computation on each step is expensive.
/// Charts are relatively small and can be used to visualize the progress of the computation.
module Primitives =

    /// See: https://stackoverflow.com/questions/49974736/how-to-declare-a-generic-exception-types-in-f
    /// We have to resort to throwing a specific exception in order
    /// to perform early termination from deep inside C# ODE solver.
    /// There seems to be no other easy and clean way. Revisit if that changes.
    type ComputationAbortedException<'P> (pd : ProgressData<'P>, ct : CancellationType) =
        inherit Exception ()

        member _.progressData = pd
        member _.cancellationType = ct


    type RunQueue<'P> =
        {
            runQueueId : RunQueueId
            runQueueStatus : RunQueueStatus
            progressData : ProgressData<'P>
            createdOn : DateTime
        }


    /// A try call back wrapper.
    type TryCallBack<'X> =
        | TryCallBack of (EvolutionTime -> 'X -> unit)

        member r.invoke = let (TryCallBack v) = r in v


    type CalculationCompletionType =
        | CompletedCalculation
        | CancelledCalculation of CancellationType


    type CallBackNotificationType =
        | ProgressNotification
        | ChartNotification
        | ChartDetailedNotification // Also should include ChartNotification.
        | ProgressAndChartNotification
        | AllNotification // Should send ProgressNotification and ChartDetailedNotification


    type CallBackType =
        | RegularCallBack
        | FinalCallBack of CalculationCompletionType


    /// We don't want to store any initial data in the chart data.
    /// Rather, we provide 'D when generating charts.
    type ChartInitData = unit


    type ChartSliceData<'C> =
        {
            t : decimal
            chartData : 'C
        }


    type ChartData<'C> = list<ChartSliceData<'C>>


    type ChartDataUpdater<'C> () =
        interface IUpdater<ChartInitData, ChartSliceData<'C>, ChartData<'C>> with
            member _.init _ = []
            member _.add a m = a :: m


    type ProgressCallBack<'P> =
        | ProgressCallBack of (CallBackType -> ProgressData<'P> -> unit)

        member r.invoke = let (ProgressCallBack v) = r in v


    type ChartGenerator<'D, 'X, 'C> =
        {
            /// A function to call in order to generate a chart data point.
            /// This is to collect "lightweight" chart data points at some predetermined intervals.
            getChartData : 'D -> EvolutionTime -> 'X -> 'C

            ///// A function to call in order to generate and store locally a chart data point.
            ///// This is to collect "lightweight" chart data points at some predetermined intervals.
            //addChartData : 'C -> unit

            /// A function to call to generate all lightweight ("evolution") charts.
            generateCharts : RunQueueId -> 'D -> ChartNotificationType -> list<ChartSliceData<'C>> -> list<Chart> option // A generator may skip generating charts if it finds them useless. Force chart generation if you need them.

            /// A function to call to generate heavy charts if any.
            /// If you don't need heavy charts, then return None.
            generateDetailedCharts : RunQueueId -> 'D -> EvolutionTime -> 'X -> list<Chart> option
        }


    type ChartCallBack =
        | ChartCallBack of (list<Chart> -> unit)

        member r.invoke = let (ChartCallBack v) = r in v


    /// A function to call to check if cancellation is requested.
    type CheckCancellation =
        | CheckCancellation of (RunQueueId -> CancellationType option)

        member r.invoke = let (CheckCancellation v) = r in v


    /// An additional [past] data needed to determine if a call back is needed.
    type NeedsCallBackData =
        {
            progressData : ProgressData
            lastCheck : DateTime
            nextProgress : decimal
            nextChartProgress : decimal
            nextChartDetailedProgress : decimal
        }

        static member defaultValue =
            {
                progressData = ProgressData.defaultValue
                lastCheck = DateTime.Now
                nextProgress = 0.0M
                nextChartProgress = 0.0M
                nextChartDetailedProgress = 0.0M
            }


    /// A function to call in order to determine if a call back is needed.
    type NeedsCallBack =
        | NeedsCallBack of (NeedsCallBackData -> EvolutionTime -> NeedsCallBackData * CallBackNotificationType option)

        member r.invoke = let (NeedsCallBack v) = r in v


    /// A proxy with all information needed to call back.
    type CallBackProxy<'P> =
        {
            progressCallBack : ProgressCallBack<'P>
            chartCallBack : ChartCallBack
            checkCancellation : CheckCancellation
        }


    /// The main solver runner function.
    /// It takes initial evolution time and computation state and returns the final evolution time and computation state.
    /// It will try to call back with progress data and chart data when it finds it necessary based on the parameters.
    type SolverRunner<'X> =
        | SolverRunner of (EvolutionTime * 'X -> TryCallBack<'X> -> EvolutionTime * 'X)

        member r.invoke = let (SolverRunner v) = r in v


    type SolverProxy<'D, 'P, 'X> =
        {
            getInitialData : 'D -> 'X // Get the initial data from the model data.
            getProgressData : ('D -> EvolutionTime -> 'X -> 'P) option // Get optional detailed progress data from the computation state.
            getInvariant : 'D -> EvolutionTime -> 'X -> RelativeInvariant // Get invariant from the computation state.
        }


    /// A user proxy to run the solver. Must be implemented by the user.
    type SolverRunnerUserProxy<'D, 'P, 'X, 'C> =
        {
            solverRunner : SolverRunner<'X> // Run the computation from the initial data till the end and report progress on the way.
            solverProxy : SolverProxy<'D, 'P, 'X>
            chartGenerator : ChartGenerator<'D, 'X, 'C>
        }


    /// A system proxy to run the solver. Is implemented by the system but can be overridden.
    type SolverRunnerSystemProxy<'D, 'P, 'X, 'C> =
        {
            callBackProxy : CallBackProxy<'P>
            addChartData : ChartSliceData<'C> -> unit
            getChartData : unit -> list<ChartSliceData<'C>>
            checkNotification : RunQueueId -> ChartNotificationType option
            clearNotification : RunQueueId -> unit
        }


    /// A model data and supporting data that is needed to run the solver.
    type RunnerData<'D> =
        {
            runQueueId : RunQueueId
            partitionerId : PartitionerId
            workerNodeId : WorkerNodeId
            messagingDataVersion : MessagingDataVersion
            modelData : ModelData<'D>
            started : DateTime
            cancellationCheckFreq : TimeSpan // How often to check if cancellation is requested.
        }


    /// Everything that we need to know how to run the solver and report progress.
    /// It seems convenient to separate evolution time (whatever it means) and a computation state, 'X
    /// The overall data consists of the model data & related data, which can be serialized / deserialized,
    /// the user proxy (must be implemented outside the library), and the system proxy (implemented in the library).
    type SolverRunnerContext<'D, 'P, 'X, 'C> =
        {
            runnerData : RunnerData<'D>
            systemProxy : SolverRunnerSystemProxy<'D, 'P, 'X, 'C>
            userProxy : SolverRunnerUserProxy<'D, 'P, 'X, 'C>
        }


    /// A system context that is needed to create the solver.
    type SolverRunnerSystemContext<'D, 'P, 'X, 'C> =
        {
            logCrit : SolverRunnerCriticalError -> UnitResult<SysError>
            workerNodeServiceInfo : WorkerNodeServiceInfo
            tryLoadRunQueue : RunQueueId -> DistributedProcessingResult<(ModelData<'D> * RunQueueStatus)>
            getAllowedSolvers : WorkerNodeInfo -> int
            checkRunning : int option -> RunQueueId -> CheckRunningResult
            tryStartRunQueue : RunQueueId -> DistributedProcessingUnitResult
            createSystemProxy : RunnerData<'D> -> SolverRunnerSystemProxy<'D, 'P, 'X, 'C>
            runSolver : SolverRunnerContext<'D, 'P, 'X, 'C> -> unit
        }


    /// A data to run the solver.
    type SolverRunnerData =
        {
            solverId : SolverId
            runQueueId : RunQueueId
            forceRun : bool
            messagingDataVersion : MessagingDataVersion
            cancellationCheckFreq : TimeSpan
        }
