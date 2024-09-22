namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.Errors
open System.Collections.Concurrent

module Runner =

    ///// Note that it is compiled into a static variable, which means that you cannot run many instances of the solver in parallel.
    ///// Currently this is not an issue since parallel running is not needed (by design).
    ///// Note (2) - it cannot be moved inside nSolve because that will require moving fUseNonNegative inside nSolve for ODE solver,
    ///// which is not allowed by IL design.
    //let mutable private needsCallBackData = NeedsCallBackData.defaultValue

    let private needsCallBackDataDictionary = new ConcurrentDictionary<RunQueueId, NeedsCallBackData>()


    let private getNeedsCallBackData runQueueId =
        match needsCallBackDataDictionary.TryGetValue(runQueueId) with
        | true, v -> v
        | false, _ -> NeedsCallBackData.defaultValue


    let private setNeedsCallBackData runQueueId data =
        needsCallBackDataDictionary.AddOrUpdate(runQueueId, data, (fun _ _ -> data)) |> ignore

    // ================================================================ //

    let private calculateProgress d (t : EvolutionTime) =
        (t.value - d.solverInputParams.startTime.value) / (d.solverInputParams.endTime.value - d.solverInputParams.startTime.value)
        |> decimal


    let private shouldNotifyByCallCount d =
        let callCount = d.progressData.callCount

        let r =
            [
                callCount <= 10L
                callCount > 10L && callCount <= 100L && callCount % 5L = 0L
                callCount > 100L && callCount <= 1_000L && callCount % 50L = 0L
                callCount > 1_000L && callCount <= 10_000L && callCount % 500L = 0L
                callCount > 10_000L && callCount <= 100_000L && callCount % 5_000L = 0L
                callCount > 100_000L && callCount <= 1_000_000L && callCount % 50_000L = 0L
                callCount > 1_000_000L && callCount <= 10_000_000L && callCount % 500_000L = 0L
                callCount > 10_000_000L && callCount <= 100_000_000L && callCount % 5_000_000L = 0L
                callCount > 100_000_000L && callCount % 50_000_000L = 0L
            ]
            |> List.tryFind id
            |> Option.defaultValue false

        // printDebug $"shouldNotifyByCallCount: callCount = {callCount}, r = {r}."
        r


    let private shouldNotifyByNextProgress d ncbd t =
        let p = calculateProgress d t
        let r = p >= ncbd.nextProgress
        // n.logger.logDebugString $"shouldNotifyByNextProgress: p = {p}, nextProgress = {d.nextProgress}, r = {r}."
        r


    let private shouldNotifyByNextChartProgress d ncbd t =
        let p = calculateProgress d t
        let r = p >= ncbd.nextChartProgress
        // n.logger.logDebugString $"shouldNotifyByNextChartProgress: p = {p}, nextChartProgress = {d.nextChartProgress}, r = {r}."
        r


    let private shouldNotifyByNextChartDetailedProgress d ncbd t =
        // n.logger.logDebugString $"shouldNotifyByNextChartDetailedProgress: t = {t}, n.odeParams.outputParams.noOfChartDetailedPoints = {n.odeParams.outputParams.noOfChartDetailedPoints}."
        match d.solverOutputParams.noOfChartDetailedPoints with
        | Some _ ->
            let p = calculateProgress d t
            let r = p >= ncbd.nextChartDetailedProgress
            // n.logger.logDebugString $"shouldNotifyByNextChartDetailedProgress: t = {t}, p = {p}, d.nextChartDetailedProgress = {d.nextChartDetailedProgress}, r = {r}."
            r
        | None -> false


    let private calculateNextProgress d t =
        let r =
            match d.solverOutputParams.noOfProgressPoints with
            | np when np <= 0 -> 1.0m
            | np -> min 1.0m ((((calculateProgress d t) * (decimal np) |> floor) + 1.0m) / (decimal np))
        // n.logger.logDebugString $"calculateNextProgress: r = {r}."
        r


    let private calculateNextChartProgress d t =
        let r =
            match d.solverOutputParams.noOfOutputPoints with
            | np when np <= 0 -> 1.0m
            | np -> min 1.0m ((((calculateProgress d t) * (decimal np) |> floor) + 1.0m) / (decimal np))
        // n.logger.logDebugString $"calculateNextChartProgress: t = {t}, r = {r}."
        r


    let private calculateNextChartDetailedProgress d t =
        let r =
            match d.solverOutputParams.noOfChartDetailedPoints with
            | Some nop ->
                let r =
                    match nop with
                    | np when np <= 0 -> 1.0m
                    | np ->
                        let progress = calculateProgress d t
                        // n.logger.logDebugString $"calculateNextChartDetailedProgress: t = {t}, progress = {progress}."
                        min 1.0m ((((calculateProgress d t) * (decimal np) |> floor) + 1.0m) / (decimal np))
                r
            | None -> 1.0m
        // n.logger.logDebugString $"calculateNextChartDetailedProgress: t = {t}, r = {r}."
        r


    let private shouldNotifyProgress d ncbd t = shouldNotifyByCallCount ncbd || shouldNotifyByNextProgress d ncbd t
    let private shouldNotifyChart d ncbd t = shouldNotifyByCallCount ncbd || shouldNotifyByNextChartProgress d ncbd t


    let private needsCallBack d =
        let f ncbd t =
            let shouldNotifyProgress = shouldNotifyProgress d ncbd t
            let shouldNotifyChart = shouldNotifyChart d ncbd t
            let shouldNotifyChartDetailed = shouldNotifyByNextChartDetailedProgress d ncbd t

            let nextProgress = calculateNextProgress d t
            let nextChartProgress = calculateNextChartProgress d t
            let nextChartDetailedProgress = calculateNextChartDetailedProgress d t
            // n.logger.logDebugString $"needsCallBack: t = {t}, d = {d}, shouldNotifyProgress = {shouldNotifyProgress}, shouldNotifyChart = {shouldNotifyChart}, shouldNotifyChartDetailed = {shouldNotifyChartDetailed}, nextChartDetailedProgress = {nextChartDetailedProgress}."

            let retVal =
                match (shouldNotifyProgress, shouldNotifyChart, shouldNotifyChartDetailed) with
                | false, false, false -> (ncbd, None)
                | false, true, false ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextChartProgress to: {nextChartProgress}, ChartNotification."
                    ( { ncbd with nextChartProgress = nextChartProgress }, Some ChartNotification)
                | true, false, false ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextProgress to: {nextProgress}, ProgressNotification."
                    ( { ncbd with nextProgress = nextProgress }, Some ProgressNotification)
                | true, true, false ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextProgress to {nextProgress}, nextChartProgress to: {nextChartProgress}, ProgressAndChartNotification."
                    ( { ncbd with nextProgress = nextProgress; nextChartProgress = nextChartProgress }, Some ProgressAndChartNotification)

                | false, _, true ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextChartProgress to {nextChartProgress}, nextChartDetailedProgress to: {nextChartDetailedProgress}, ChartDetailedNotification."
                    ( { ncbd with nextChartProgress = nextChartProgress; nextChartDetailedProgress = nextChartDetailedProgress }, Some ChartDetailedNotification)
                | true, _, true ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextProgress to {nextProgress}, nextChartProgress to {nextChartProgress}, nextChartDetailedProgress to: {nextChartDetailedProgress}, AllNotification."
                    ( { ncbd with nextProgress = nextProgress; nextChartProgress = nextChartProgress; nextChartDetailedProgress = nextChartDetailedProgress }, Some AllNotification)

            // n.logger.logDebugString $"needsCallBack: retVal = {retVal}."
            retVal

        NeedsCallBack f


    let private checkCancellation d ncbd =
        let fromLastCheck = DateTime.Now - ncbd.lastCheck
        // n.logger.logDebugString $"checkCancellation: runQueueId = %A{n.runQueueId}, time interval from last check = %A{fromLastCheck}."

        if fromLastCheck > d.callBackInfo.checkCancellation.checkFreq
        then
            let cancel = d.callBackInfo.checkCancellation.invoke d.runQueueId
            { ncbd with lastCheck = DateTime.Now}, cancel
        else ncbd, None


    let private estCompl d (t : EvolutionTime) =
        match estimateEndTime (calculateProgress d t) d.solverInputParams.started with
        | Some e -> " est. compl.: " + e.ToShortDateString() + ", " + e.ToShortTimeString() + ","
        | None -> EmptyString


    let private calculateProgressDataWithErr ncbd (t : EvolutionTime) v =
        // n.logger.logDebugString $"calculateProgressDataWithErr: Called with t = {t}, v = {v}."

        let withMessage s m =
            let eo =
                match s with
                | Some v -> $"m: '{m}', t: {t}, Message: '{v}'."
                | None -> m
                |> ErrorMessage
                |> Some

            let pd = { ncbd.progressData with errorMessageOpt = eo}
            pd

        match v with
        | AbortCalculation s -> $"The run queue was aborted at: %.2f{ncbd.progressData.progress * 100.0m}%% progress." |> withMessage s
        | CancelWithResults s ->
            //$"The run queue was cancelled at: %.2f{d.progressData.progressData.progress * 100.0m}%% progress. Absolute tolerance: {n.odeParams.absoluteTolerance}."
            $"The run queue was cancelled at: %.2f{ncbd.progressData.progress * 100.0m}%% progress."
            |> withMessage s


    let private notifyAll d c cbd =
        d.callBackInfo.progressCallBack.invoke c cbd
        d.callBackInfo.chartDetailedCallBack.invoke c cbd


    let private tryCallBack d ncbd p ri (t : EvolutionTime) x =
        //let d0 = needsCallBackData
        // n.logger.logDebugString $"tryCallBack - starting: t = {t}, needsCallBackData = {d0}."
        //let pd = { d0.progressData with callCount = d0.progressData.callCount + 1L; progress = calculateProgress n t }

        let pd =
            {
                progressData =
                    {
                        progress = calculateProgress d t
                        callCount = ncbd.progressData.callCount + 1L
                        t = t
                        relativeInvariant = ri t x
                        errorMessageOpt = None
                    }
                progressDetailed = p |> Option.map (fun e -> e t x) // |> Some // Calculates detailed progress.
            }

        let ncbd1 = { ncbd with progressData = pd.progressData }
        let ncbd2, ct = checkCancellation d ncbd1

        let cbd = { progressData = pd; x = x }
        // n.logger.logDebugString $"    tryCallBack: t = {t}, d = {d}, cbd = {cbd}."

        match ct with
        | Some v ->
            notifyAll d (v |> CancelledCalculation |> FinalCallBack) cbd
            let progressDataWithErr = calculateProgressDataWithErr ncbd1 t v
            raise(ComputationAbortedException (progressDataWithErr, v))
        | None ->
            // let c, v = n.callBackInfo.needsCallBack.invoke d t
            let ncbd3, v = (needsCallBack d).invoke ncbd2 t
            // n.logger.logDebugString $"    tryCallBack: t = {t}, setting needsCallBackData to c = {c}, v = {v}."

            match v with
            | None -> ()
            | Some v ->
                let i = d.callBackInfo

                match v with
                | ProgressNotification -> i.progressCallBack.invoke RegularCallBack cbd
                | ChartNotification -> i.chartCallBack.invoke RegularCallBack cbd
                | ChartDetailedNotification -> i.chartDetailedCallBack.invoke RegularCallBack cbd
                | ProgressAndChartNotification ->
                    i.progressCallBack.invoke RegularCallBack cbd
                    i.chartCallBack.invoke RegularCallBack cbd
                | AllNotification -> notifyAll d RegularCallBack cbd

            ncbd3


    let runSover<'D, 'P, 'X, 'C> (d : SolverRunnerData<'D, 'P, 'X, 'C>) =
        // (n : SolverParams<'P, 'X>, 
        let getProgressData t x =
            {
                progressData = (getNeedsCallBackData d.runQueueId).progressData
                progressDetailed = d.solverData.getProgressData |> Option.map (fun e -> e t x)
            }

        let updateNeedsCallBackData t v =
            let ncbd = getNeedsCallBackData d.runQueueId
            let ncbd1 = tryCallBack d ncbd (d.solverData.getProgressData) (d.solverData.getInvariant) t v
            setNeedsCallBackData d.runQueueId ncbd1

        let tryCallBack : TryCallBack<'X> = TryCallBack updateNeedsCallBackData

        // Calculate initial progress, including additional progress data, and notify about beginning of computation.
        let (t0, x0) = d.solverData.getInitialData d.modelData
        updateNeedsCallBackData t0 x0
        let cbdStart = { progressData = getProgressData t0 x0; x = x0 }
        notifyAll d RegularCallBack cbdStart

        // Run the computation from the initial data till the end and report progress on the way.
        let (tEnd, xEnd) = d.solverData.run (t0, x0) tryCallBack

        // Calculate final progress, including additional progress data, and notify about completion of computation.
        let cbdEnd = { progressData = getProgressData tEnd xEnd; x = xEnd }
        notifyAll d (FinalCallBack CompletedCalculation) cbdEnd

        (tEnd, xEnd)
