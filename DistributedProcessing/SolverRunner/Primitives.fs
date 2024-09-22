namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.DistributedProcessing.Primitives
open Softellect.Sys.Primitives
open Softellect.Sys.Core

/// A SolverRunner operates on input data 'D and produces progress / output data 'P.
/// Internally it uses a data type 'X to store the state of the computation and may produce chart data 'C.
/// So, 4 generics are used: 'D, 'P, 'X, 'C and it is a minimum that's needed.
/// "Charts" are some slices of data that are produced during the computation and can be used to visualize the progress.
/// We do it that way because both 'D and 'X could be huge and so capturing the state of the computation on each step is expensive.
/// Charts are relatively small and can be used to visualize the progress of the computation.
module Primitives =

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


    /// An evolution-like call-back data.
    type CallBackData<'P, 'X> =
        {
            progressData : ProgressData<'P>
            x : 'X
        }


    /// A function to call in order to notify about progress.
    type ProgressCallBack<'P, 'X> =
        | ProgressCallBack of (CallBackType -> CallBackData<'P, 'X> -> unit)

        member r.invoke = let (ProgressCallBack v) = r in v


    /// A function to call in order to generate a chart data point.
    type ChartCallBack<'P, 'X> =
        | ChartCallBack of (CallBackType -> CallBackData<'P, 'X> -> unit)

        member r.invoke = let (ChartCallBack v) = r in v


    /// A function to call in order to generate a detailed chart data point.
    /// This is mostly to collect "heavy" 3D data at some predetermined intervals.
    type ChartDetailedCallBack<'P, 'X> =
        | ChartDetailedCallBack of (CallBackType -> CallBackData<'P, 'X> -> unit)

        member r.invoke = let (ChartDetailedCallBack v) = r in v


    /// A function to call to check if cancellation is requested.
    type CheckCancellation =
        {
            invoke : RunQueueId -> CancellationType option
            checkFreq : TimeSpan // How often to check if cancellation is requested.
        }

        static member defaultValue =
            {
                invoke = fun _ -> None
                checkFreq = TimeSpan.FromMinutes(5.0)
            }


    /// An addition [past] data needed to determine if a call back is needed.
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


    /// All information needed to call back.
    type CallBackInfo<'P, 'X, 'C> =
        {
            progressCallBack : ProgressCallBack<'P, 'X>
            chartDataUpdater : IAsyncUpdater<'X, 'C> // Update chart data.
            chartCallBack : ChartCallBack<'P, 'X>
            chartDetailedCallBack : ChartDetailedCallBack<'P, 'X>
            checkCancellation : CheckCancellation
        }


    //type DerivativeCalculator =
    //    | OneByOne of (double -> double[] -> int -> double)
    //    | FullArray of (double -> double[] -> double[])
    //
    //    member d.calculate t x =
    //        match d with
    //        | OneByOne f -> x |> Array.mapi (fun i _ -> f t x i)
    //        | FullArray f -> f t x
    //
    //type AlgLibMethod =
    //    | CashCarp
    //
    //
    //type OdePackMethod =
    //    | Adams
    //    | Bdf
    //
    //    member t.value =
    //        match t with
    //        | Adams -> 1
    //        | Bdf -> 2
    //
    //
    //type CorrectorIteratorType =
    //    | Functional
    //    | ChordWithDiagonalJacobian
    //
    //    member t.value =
    //        match t with
    //        | Functional -> 0
    //        | ChordWithDiagonalJacobian -> 3
    //
    //
    //type NegativeValuesCorrectorType =
    //    | DoNotCorrect
    //    | UseNonNegative of double
    //
    //    member nc.value =
    //        match nc with
    //        | DoNotCorrect -> 0
    //        | UseNonNegative _ -> 1
    //
    //    member nc.correction =
    //        match nc with
    //        | DoNotCorrect -> 0.0
    //        | UseNonNegative c -> c
    //
    //
    //type SolverType =
    //    | AlgLib of AlgLibMethod
    //    | OdePack of OdePackMethod * CorrectorIteratorType * NegativeValuesCorrectorType
    //
    //    member t.correction =
    //        match t with
    //        | AlgLib _ -> 0.0
    //        | OdePack (_, _, nc) -> nc.correction


    type SolverInputParams =
        {
            started : DateTime
            startTime : EvolutionTime
            endTime : EvolutionTime
        }


    type SolverOutputParams =
        {
            noOfOutputPoints : int
            noOfProgressPoints : int
            noOfChartDetailedPoints : int option
        }

        static member defaultValue =
            {
                noOfOutputPoints = 2
                noOfProgressPoints = 100
                noOfChartDetailedPoints = None
            }


    //type SolverParams<'P, 'X> =
    //    {
    //        runQueueId : RunQueueId
    //        solveInputParams : SolveInputParams
    //        solverOutputParams : SolverOutputParams
    //        callBackInfo : CallBackInfo<'P, 'X>
    //        started : DateTime
    //    }


    /// Everything that we need to know how to run the solver.
    type SolverData<'D, 'P, 'X> =
        {
            getInitialData : 'D -> (EvolutionTime * 'X) // Get the initial data from the model data.
            getProgressData : (EvolutionTime -> 'X -> 'P) option // Get optional detailed progress data from the computation state.
            getInvariant : EvolutionTime -> 'X -> RelativeInvariant // Get invariant from the computation state.
            run : (EvolutionTime * 'X) -> TryCallBack<'X> -> (EvolutionTime * 'X) // Run the computation from the initial data till the end and report progress on the way.
        }


    /// Everything that we need to know how to run the solver and report progress.
    /// It seems convenient to separate evolution time (whatever it means) and a computation state, 'X
    type SolverRunnerData<'D, 'P, 'X, 'C> =
        {
            runQueueId : RunQueueId
            modelData : 'D // All data that we need to run the model.
            solverInputParams : SolverInputParams
            solverOutputParams : SolverOutputParams
            callBackInfo : CallBackInfo<'P, 'X, 'C>
            solverData : SolverData<'D, 'P, 'X>

            //noOfProgressPoints : int // Number of progress points to report. Default is 100.
            //noOfChartPoints : int option // Charts are optional.
            //getInitialData : 'D -> (EvolutionTime * 'X) // Get the initial data from the model data.
            //getProgressData : (EvolutionTime -> 'X -> 'P) option // Get optional detailed progress data from the computation state.
            //getInvariant : EvolutionTime -> 'X -> RelativeInvariant // Get invariant from the computation state.
            //run : (EvolutionTime * 'X) -> TryCallBack<'X> -> (EvolutionTime * 'X) // Run the computation from the initial data till the end and report progress on the way.
            //progressCallBack : RunQueueStatus option -> ProgressData<'P> -> unit // Report progress if needed.
            //chartDataUpdater : IAsyncUpdater<'X, 'C> // Update chart data.
            //checkCancellation : RunQueueId -> CancellationType option // Checks if cancellation is requested.
            //checkFreq : TimeSpan // How often to check if cancellation is requested.
        }


    //type OdeParams =
    //    {
    //        startTime : double
    //        endTime : double
    //        stepSize : double
    //        absoluteTolerance : AbsoluteTolerance
    //        solverType : SolverType
    //        outputParams : OdeOutputParams
    //    }


    //type NSolveParam =
    //    {
    //        odeParams : OdeParams
    //        runQueueId : RunQueueId
    //        initialValues : double[]
    //        derivative : DerivativeCalculator
    //        callBackInfo : CallBackInfo
    //        started : DateTime
    //        logger : Logger<int>
    //    }
