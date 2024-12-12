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
/// Internally it uses a data type 'X to store the state of the computation and may produce result data 'C.
/// So, 4 generics are used: 'D, 'P, 'X, 'C and it is a minimum that's needed.
/// "Results" are some slices of data that are produced during the computation and can be used to visualize the progress.
/// We do it that way because both 'D and 'X could be huge and so capturing the state of the computation on each step is expensive.
/// Results are relatively small and can be used to visualize the progress of the computation.
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
        | ResultNotification
        | ResultDetailedNotification // Also should include ResultNotification.
        | ProgressAndResultNotification
        | AllNotification // Should send ProgressNotification and ResultDetailedNotification


    type CallBackType =
        | RegularCallBack
        | FinalCallBack of CalculationCompletionType


    /// We don't want to store any initial data in the result data.
    /// Rather, we provide 'D when generating results.
    type ResultInitData = unit


    type ResultSliceData<'C> =
        {
            t : decimal
            resultData : 'C
        }


    type ResultData<'C> = list<ResultSliceData<'C>>


    type ResultDataUpdater<'C> () =
        interface IUpdater<ResultInitData, ResultSliceData<'C>, ResultData<'C>> with
            member _.init _ = []
            member _.add a m = a :: m


    type ProgressCallBack<'P> =
        | ProgressCallBack of (CallBackType -> ProgressData<'P> -> unit)

        member r.invoke = let (ProgressCallBack v) = r in v


    type ResultGenerator<'D, 'X, 'C> =
        {
            /// A function to call in order to generate a result data point.
            /// This is to collect "lightweight" reslut data points at some predetermined intervals.
            getResultData : 'D -> EvolutionTime -> 'X -> 'C

            /// A function to call to generate all lightweight ("evolution") results.
            /// A generator may skip generating results if it finds them useless. Force result generation if you need them.
            generateResults : RunQueueId -> 'D -> ResultNotificationType -> list<ResultSliceData<'C>> -> list<CalculationResult> option

            /// A function to call to generate heavy results if any.
            /// If you don't need heavy results, then return None.
            generateDetailedResults : RunQueueId -> 'D -> EvolutionTime -> 'X -> list<CalculationResult> option
        }


    type ResultCallBack =
        | ResultCallBack of (list<CalculationResult> -> unit)

        member r.invoke = let (ResultCallBack v) = r in v


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
            nextResultProgress : decimal
            nextResultDetailedProgress : decimal
        }

        static member defaultValue =
            {
                progressData = ProgressData.defaultValue
                lastCheck = DateTime.Now
                nextProgress = 0.0M
                nextResultProgress = 0.0M
                nextResultDetailedProgress = 0.0M
            }


    /// A function to call in order to determine if a call back is needed.
    type NeedsCallBack =
        | NeedsCallBack of (NeedsCallBackData -> EvolutionTime -> NeedsCallBackData * CallBackNotificationType option)

        member r.invoke = let (NeedsCallBack v) = r in v


    /// A proxy with all information needed to call back.
    type CallBackProxy<'P> =
        {
            progressCallBack : ProgressCallBack<'P>
            resultCallBack : ResultCallBack
            checkCancellation : CheckCancellation
        }


    /// The main solver runner function.
    /// It takes initial evolution time and computation state and returns the final evolution time and computation state.
    /// It will try to call back with progress data and result data when it finds it necessary based on the parameters.
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
            resultGenerator : ResultGenerator<'D, 'X, 'C>
        }


    /// A system proxy to run the solver. Is implemented by the system but can be overridden.
    type SolverRunnerSystemProxy<'P, 'X, 'C> =
        {
            callBackProxy : CallBackProxy<'P>
            addResultData : ResultSliceData<'C> -> unit
            getResultData : unit -> list<ResultSliceData<'C>>
            checkNotification : RunQueueId -> ResultNotificationType option
            clearNotification : RunQueueId -> unit
        }


    type RunnerData =
        {
            runQueueId : RunQueueId
            partitionerId : PartitionerId
            workerNodeId : WorkerNodeId
            messagingDataVersion : MessagingDataVersion
            started : DateTime
            cancellationCheckFreq : TimeSpan // How often to check if cancellation is requested.
        }


    /// A model data and supporting data that is needed to run the solver.
    type RunnerData<'D> =
        {
            runnerData : RunnerData
            modelData : ModelData<'D>
        }


    /// Everything that we need to know how to run the solver and report progress.
    /// It seems convenient to separate evolution time (whatever it means) and a computation state, 'X
    /// The overall data consists of the model data & related data, which can be serialized / deserialized,
    /// the user proxy (must be implemented outside the library), and the system proxy (implemented in the library).
    type SolverRunnerContext<'D, 'P, 'X, 'C> =
        {
            runnerData : RunnerData<'D>
            systemProxy : SolverRunnerSystemProxy<'P, 'X, 'C>
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
            createSystemProxy : RunnerData -> SolverRunnerSystemProxy<'P, 'X, 'C>
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
