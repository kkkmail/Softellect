namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.Messaging.Primitives

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
        inherit System.Exception ()

        member _.progressData = pd
        member _.cancellationType = ct


    type ProgressUpdateInfo<'P> =
        {
            runQueueId : RunQueueId
            updatedRunQueueStatus : RunQueueStatus option
            progressData : ProgressData<'P>
        }

        member p.toProgressUpdateInfo() : ProgressUpdateInfo =
            {
                runQueueId = p.runQueueId
                updatedRunQueueStatus = p.updatedRunQueueStatus
                progressData = p.progressData.toProgressData()
            }


    /// All data that we need in order to run a model.
    /// The underlying model data is of type 'D.
    /// And we have solver input parameters and solver output parameters to control the evolution and what we output.
    type ModelData<'D> =
        {
            solverInputParams : SolverInputParams
            solverOutputParams : SolverOutputParams
            solverId : SolverId
            modelData : 'D
        }

        member d.toModelBinaryData() : ModelBinaryData =
            {
                solverInputParams = d.solverInputParams
                solverOutputParams = d.solverOutputParams
                modelData = d.modelData |> serializeData
            }

        static member tryFromModelBinaryData solverId (m : ModelBinaryData) =
            match tryDeserializeData<'D> m.modelData with
            | Ok modelData ->
                {
                    solverInputParams = m.solverInputParams
                    solverOutputParams = m.solverOutputParams
                    solverId = solverId
                    modelData = modelData
                } |> Ok
            | Error e -> Error e


    type RunQueue<'P> =
        {
            runQueueId : RunQueueId
            runQueueStatus : RunQueueStatus
            progressData : ProgressData<'P>
            createdOn : DateTime
        }


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
        //{
        //    //initData : 'D // TODO kk:20240927 - Store more (like tEnd)???
        //    allChartData : list<ChartSliceData<'C>>
        //}

        /// Last calculated value of tEnd.
        //member cd.tLast =
        //    match cd.allChartData |> List.tryHead with
        //    | Some c -> c.t
        //    | None -> 0.0m


        //member cd.progress =
        //    let tEnd = cd.initData.tEnd
        //    min (max (if tEnd > 0.0m then cd.tLast / tEnd else 0.0m) 0.0m) 1.0m


    type ChartDataUpdater<'C> () =
        interface IUpdater<ChartInitData, ChartSliceData<'C>, ChartData<'C>> with
            member _.init _ = []
            member _.add a m = a :: m


    ///// An evolution-like call-back data.
    ///// Evolution time is inside ProgressData
    //type CallBackData<'P, 'X> =
    //    {
    //        progressData : ProgressData<'P>
    //        x : 'X
    //    }


    /// A function to call in order to notify about progress.
    //type ProgressCallBack<'P, 'X> =
    //    | ProgressCallBack of (CallBackType -> CallBackData<'P, 'X> -> unit)
    //type ProgressCallBack<'P> =
    //    | ProgressCallBack of (CallBackType -> ProgressData<'P> -> unit)
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
            generateCharts : 'D -> ChartNotificationType -> list<ChartSliceData<'C>> -> list<Chart> option // A generator may skip generating charts if it finds them useless. Force chart generation if you need them.

            /// A function to call to generate heavy charts.
            generateDetailedCharts : 'D -> EvolutionTime -> 'X -> list<Chart>
        }


    type ChartCallBack =
        | ChartCallBack of (list<Chart> -> unit)

        member r.invoke = let (ChartCallBack v) = r in v

        //{

        //    ///
        //    chartCallBack : (CallBackType -> CallBackData<'P, 'X> -> unit)

        //    /// A function to call in order to generate and store a detailed chart data point.
        //    /// This is to collect "heavy", e.g., 3D data at some predetermined intervals.
        //    /// A heavy chart should be send back one by one. if any post processing is needed,
        //    /// then it should be done by partitioner.
        //    chartDetailedCallBack : (CallBackType -> CallBackData<'P, 'X> -> unit)
        //}


    /// A function to call to check if cancellation is requested.
    type CheckCancellation =
        | CheckCancellation of (RunQueueId -> CancellationType option)

        member r.invoke = let (CheckCancellation v) = r in v


    //type CheckCancellation =
    //    {
    //        invoke : RunQueueId -> CancellationType option
    //        checkFreq : TimeSpan // How often to check if cancellation is requested.
    //    }

    //    static member defaultValue =
    //        {
    //            invoke = fun _ -> None
    //            checkFreq = TimeSpan.FromMinutes(5.0)
    //        }


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


    /// A proxy with all information needed to call back.
    type CallBackProxy<'P> =
        {
            progressCallBack : ProgressCallBack<'P>
            chartCallBack : ChartCallBack
            checkCancellation : CheckCancellation
        }


    type SolverRunner<'X> =
        | SolverRunner of ((EvolutionTime * 'X) -> TryCallBack<'X> -> (EvolutionTime * 'X))

        member r.invoke = let (SolverRunner v) = r in v


    type SolverProxy<'D, 'P, 'X> =
        {
            getInitialData : 'D -> 'X // Get the initial data from the model data.
            getProgressData : ('D -> EvolutionTime -> 'X -> 'P) option // Get optional detailed progress data from the computation state.
            getInvariant : 'D -> EvolutionTime -> 'X -> RelativeInvariant // Get invariant from the computation state.
        }


    /// A user proxy to run the solver. Must be implemented by the user.
    type UserProxy<'D, 'P, 'X, 'C> =
        {
            solverRunner : SolverRunner<'X> // Run the computation from the initial data till the end and report progress on the way.
            solverProxy : SolverProxy<'D, 'P, 'X>
            chartGenerator : ChartGenerator<'D, 'X, 'C>
        }


    /// A system proxy to run the solver. Is implemented by the system but can be overridden.
    type SystemProxy<'D, 'P, 'X, 'C> =
        {
            callBackProxy : CallBackProxy<'P>
            addChartData : ChartSliceData<'C> -> unit
            getChartData : unit -> list<ChartSliceData<'C>>
            checkNotification : RunQueueId -> ChartNotificationType option
            clearNotification : RunQueueId -> unit
        }


    /// A model data and supportting data that is needed to run the solver.
    type RunnerData<'D> =
        {
            runQueueId : RunQueueId
            partitionerId : PartitionerId
            messagingDataVersion : MessagingDataVersion
            modelData : ModelData<'D>
            started : DateTime
            cancellationCheckFreq : TimeSpan // How often to check if cancellation is requested.
        }


    /// Everything that we need to know how to run the solver and report progress.
    /// It seems convenient to separate evolution time (whatever it means) and a computation state, 'X
    /// The overall data consists of the model data & related data, which can be serialized / deserialized,
    // the user proxy (must be implemented outside the library), and the system proxy (implemented in the library).
    type SolverRunnerContext<'D, 'P, 'X, 'C> =
        {
            runnerData : RunnerData<'D>
            systemProxy : SystemProxy<'D, 'P, 'X, 'C>
            userProxy : UserProxy<'D, 'P, 'X, 'C>
        }


    // ==========================================
    // ODE Solver
    // ==========================================

    type DerivativeCalculator =
        | OneByOne of (double -> double[] -> int -> double)
        | FullArray of (double -> double[] -> double[])
    
        member d.calculate t x =
            match d with
            | OneByOne f -> x |> Array.mapi (fun i _ -> f t x i)
            | FullArray f -> f t x
    
    type AlgLibMethod =
        | CashCarp
    
    
    type OdePackMethod =
        | Adams
        | Bdf
    
        member t.value =
            match t with
            | Adams -> 1
            | Bdf -> 2
    
    
    type CorrectorIteratorType =
        | Functional
        | ChordWithDiagonalJacobian
    
        member t.value =
            match t with
            | Functional -> 0
            | ChordWithDiagonalJacobian -> 3
    
    
    type NegativeValuesCorrectorType =
        | DoNotCorrect
        | UseNonNegative of double
    
        member nc.value =
            match nc with
            | DoNotCorrect -> 0
            | UseNonNegative _ -> 1
    
        member nc.correction =
            match nc with
            | DoNotCorrect -> 0.0
            | UseNonNegative c -> c
    
    
    type SolverType =
        | AlgLib of AlgLibMethod
        | OdePack of OdePackMethod * CorrectorIteratorType * NegativeValuesCorrectorType
    
        member t.correction =
            match t with
            | AlgLib _ -> 0.0
            | OdePack (_, _, nc) -> nc.correction


    type OdeParams =
        {
            //startTime : double
            //endTime : double
            stepSize : double
            absoluteTolerance : AbsoluteTolerance
            solverType : SolverType
            derivative : DerivativeCalculator
        }


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
