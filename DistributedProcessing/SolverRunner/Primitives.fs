namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.DistributedProcessing.Primitives
open Softellect.Sys.Primitives

/// A SolverRunner operates on input data 'D and produces progress / output data 'P.
/// Internally it uses a data type 'T to store the state of the computation and may produce chart data 'C.
/// So, 4 generics are used: 'D, 'P, 'T, 'C and it is a minimum that's needed.
/// "Charts" are some slices of data that are produced during the computation and can be used to visualize the progress.
/// We do it that way because both 'D and 'T could be huge and so capturing the state of the computation on each step is expensive.
/// Charts are relatively small and can be used to visualize the progress of the computation.
module Primitives =

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


    type CallBackData<'P, 'T> =
        {
            progressData : ProgressData<'P>
            t : decimal
            x : 'T
        }


    /// A function to call in order to notify about progress.
    type ProgressCallBack<'P, 'T> =
        | ProgressCallBack of (CallBackType -> CallBackData<'P, 'T> -> unit)

        member r.invoke = let (ProgressCallBack v) = r in v


    /// A function to call in order to generate a chart data point.
    type ChartCallBack<'P, 'T> =
        | ChartCallBack of (CallBackType -> CallBackData<'P, 'T> -> unit)

        member r.invoke = let (ChartCallBack v) = r in v


    /// A function to call in order to generate a detailed chart data point.
    /// This is mostly to collect "heavy" 3D data at some predetermined intervals.
    type ChartDetailedCallBack<'P, 'T> =
        | ChartDetailedCallBack of (CallBackType -> CallBackData<'P, 'T> -> unit)

        member r.invoke = let (ChartDetailedCallBack v) = r in v


    /// A function to call to check if cancellation is requested.
    type CheckCancellation =
        | CheckCancellation of (RunQueueId -> CancellationType option)

        member r.invoke = let (CheckCancellation v) = r in v


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
        | NeedsCallBack of (NeedsCallBackData -> decimal -> NeedsCallBackData * CallBackNotificationType option)

        member r.invoke = let (NeedsCallBack v) = r in v


    type CallBackInfo<'P, 'T> =
        {
            checkFreq : TimeSpan
            progressCallBack : ProgressCallBack<'P, 'T>
            chartCallBack : ChartCallBack<'P, 'T>
            chartDetailedCallBack : ChartDetailedCallBack<'P, 'T>
            checkCancellation : CheckCancellation
        }


    //type DerivativeCalculator =
    //    | OneByOne of (double -> double[] -> int -> double)
    //    | FullArray of (double -> double[] -> double[])

    //    member d.calculate t x =
    //        match d with
    //        | OneByOne f -> x |> Array.mapi (fun i _ -> f t x i)
    //        | FullArray f -> f t x

    //type AlgLibMethod =
    //    | CashCarp


    //type OdePackMethod =
    //    | Adams
    //    | Bdf

    //    member t.value =
    //        match t with
    //        | Adams -> 1
    //        | Bdf -> 2


    //type CorrectorIteratorType =
    //    | Functional
    //    | ChordWithDiagonalJacobian

    //    member t.value =
    //        match t with
    //        | Functional -> 0
    //        | ChordWithDiagonalJacobian -> 3


    //type NegativeValuesCorrectorType =
    //    | DoNotCorrect
    //    | UseNonNegative of double

    //    member nc.value =
    //        match nc with
    //        | DoNotCorrect -> 0
    //        | UseNonNegative _ -> 1

    //    member nc.correction =
    //        match nc with
    //        | DoNotCorrect -> 0.0
    //        | UseNonNegative c -> c


    //type SolverType =
    //    | AlgLib of AlgLibMethod
    //    | OdePack of OdePackMethod * CorrectorIteratorType * NegativeValuesCorrectorType

    //    member t.correction =
    //        match t with
    //        | AlgLib _ -> 0.0
    //        | OdePack (_, _, nc) -> nc.correction


    type SolveInputParams =
        {
            startTime : decimal
            endTime : decimal
        }


    type SolverOutputParams =
        {
            noOfOutputPoints : int
            noOfProgressPoints : int
            noOfChartDetailedPoints : int option
        }


    type SolverParams<'P, 'T> =
        {
            runQueueId : RunQueueId
            solveInputParams : SolveInputParams
            solverOutputParams : SolverOutputParams
            callBackInfo : CallBackInfo<'P, 'T>
            started : DateTime
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
