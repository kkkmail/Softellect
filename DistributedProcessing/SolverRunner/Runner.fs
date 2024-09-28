namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.Errors
open System.Collections.Concurrent
open Softellect.Sys.TimerEvents
open Softellect.Sys.Rop

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

    let private calculateProgress (i : SolverInputParams) (t : EvolutionTime) =
        (t.value - i.startTime.value) / (i.endTime.value - i.startTime.value)
        |> decimal


    let private shouldNotifyByCallCount ncbd =
        let callCount = ncbd.progressData.callCount

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


    let private shouldNotifyByNextProgress i ncbd t =
        let p = calculateProgress i t
        let r = p >= ncbd.nextProgress
        // n.logger.logDebugString $"shouldNotifyByNextProgress: p = {p}, nextProgress = {d.nextProgress}, r = {r}."
        r


    let private shouldNotifyByNextChartProgress i ncbd t =
        let p = calculateProgress i t
        let r = p >= ncbd.nextChartProgress
        // n.logger.logDebugString $"shouldNotifyByNextChartProgress: p = {p}, nextChartProgress = {d.nextChartProgress}, r = {r}."
        r


    let private shouldNotifyByNextChartDetailedProgress i o ncbd t =
        // n.logger.logDebugString $"shouldNotifyByNextChartDetailedProgress: t = {t}, n.odeParams.outputParams.noOfChartDetailedPoints = {n.odeParams.outputParams.noOfChartDetailedPoints}."
        match o.noOfChartDetailedPoints with
        | Some _ ->
            let p = calculateProgress i t
            let r = p >= ncbd.nextChartDetailedProgress
            // n.logger.logDebugString $"shouldNotifyByNextChartDetailedProgress: t = {t}, p = {p}, d.nextChartDetailedProgress = {d.nextChartDetailedProgress}, r = {r}."
            r
        | None -> false


    let calculateNextProgressImpl i t nop =
        match nop with
        | np when np <= 0 -> 1.0m
        | np ->
            let progress = calculateProgress i t
            min 1.0m (((progress * (decimal np) |> floor) + 1.0m) / (decimal np))


    let private calculateNextProgress i o t =
        let r = calculateNextProgressImpl i t o.noOfProgressPoints
        // n.logger.logDebugString $"calculateNextProgress: r = {r}."
        r


    let private calculateNextChartProgress i o t =
        let r = calculateNextProgressImpl i t o.noOfOutputPoints
        // n.logger.logDebugString $"calculateNextChartProgress: t = {t}, r = {r}."
        r


    let private calculateNextChartDetailedProgress i o t =
        let r =
            match o.noOfChartDetailedPoints with
            | Some nop -> calculateNextProgressImpl i t nop
            | None -> 1.0m
        // n.logger.logDebugString $"calculateNextChartDetailedProgress: t = {t}, r = {r}."
        r


    let private shouldNotifyProgress i ncbd t = shouldNotifyByCallCount ncbd || shouldNotifyByNextProgress i ncbd t
    let private shouldNotifyChart i ncbd t = shouldNotifyByCallCount ncbd || shouldNotifyByNextChartProgress i ncbd t


    let private needsCallBack i o =
        let f ncbd t =
            let shouldNotifyProgress = shouldNotifyProgress i ncbd t
            let shouldNotifyChart = shouldNotifyChart i ncbd t
            let shouldNotifyChartDetailed = shouldNotifyByNextChartDetailedProgress i o ncbd t

            let nextProgress = calculateNextProgress  i o t
            let nextChartProgress = calculateNextChartProgress i o t
            let nextChartDetailedProgress = calculateNextChartDetailedProgress i o t
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


    let private checkCancellation runQueueId checkFreq (c : CheckCancellation) ncbd =
        let fromLastCheck = DateTime.Now - ncbd.lastCheck
        // n.logger.logDebugString $"checkCancellation: runQueueId = %A{n.runQueueId}, time interval from last check = %A{fromLastCheck}."

        if fromLastCheck > checkFreq
        then
            let cancel = c.invoke runQueueId
            { ncbd with lastCheck = DateTime.Now}, cancel
        else ncbd, None


    //let private estCompl d (t : EvolutionTime) =
    //    match estimateEndTime (calculateProgress d t) d.started with
    //    | Some e -> " est. compl.: " + e.ToShortDateString() + ", " + e.ToShortTimeString() + ","
    //    | None -> EmptyString


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


    let private notifyProgress s cb pd =
        s.callBackProxy.progressCallBack.invoke cb pd


    let private updateCharts ctx t x =
        let u = ctx.userProxy
        let s = ctx.systemProxy

        let modelData = ctx.runnerData.modelData.modelData

        let cd = u.chartGenerator.getChartData modelData t x
        s.addChartData cd


    let private notifyChartsDetailed ctx t x =
        let u = ctx.userProxy
        let s = ctx.systemProxy

        let modelData = ctx.runnerData.modelData.modelData
        let c = u.chartGenerator.generateDetailedCharts modelData t x
        s.callBackProxy.chartCallBack.invoke c


    let private notifyAll ctx cb pd t x =
        let s = ctx.systemProxy
        notifyProgress s cb pd
        updateCharts ctx t x
        notifyChartsDetailed ctx t x


    let private notifyCharts ctx t =
        let u = ctx.userProxy
        let s = ctx.systemProxy

        let modelData = ctx.runnerData.modelData.modelData
        let cd = s.getChartData()

        match u.chartGenerator.generateCharts modelData t cd with
        | Some c -> s.callBackProxy.chartCallBack.invoke c
        | None -> ignore()


    let private notifyOfCharts ctx =
        let s = ctx.systemProxy
        let runQueueId = ctx.runnerData.runQueueId

        match s.checkNotification runQueueId with
        | Some t ->
            // TODO kk:20240926 - handle errors.
            let r1 = notifyCharts ctx t
            let r2 = s.clearNotification runQueueId
            //combineUnitResults (DistributedProcessingError.addError) r1 r2
            Ok()
        | None -> Ok()


    let private tryCallBack ctx ncbd (t : EvolutionTime) x =
        let d = ctx.runnerData
        let u = ctx.userProxy
        let s = ctx.systemProxy

        let runQueueId = d.runQueueId
        let modelData = d.modelData.modelData
        let i = d.modelData.solverInputParams
        let o = d.modelData.solverOutputParams
        let c = s.callBackProxy.checkCancellation

        //let d0 = needsCallBackData
        // n.logger.logDebugString $"tryCallBack - starting: t = {t}, needsCallBackData = {d0}."
        //let pd = { d0.progressData with callCount = d0.progressData.callCount + 1L; progress = calculateProgress n t }

        let pd =
            {
                progressData =
                    {
                        progress = calculateProgress i t
                        callCount = ncbd.progressData.callCount + 1L
                        t = t
                        relativeInvariant = u.solverProxy.getInvariant modelData t x
                        errorMessageOpt = None
                    }
                progressDetailed = u.solverProxy.getProgressData |> Option.map (fun e -> e modelData t x) // Calculates detailed progress.
            }

        let ncbd1 = { ncbd with progressData = pd.progressData }
        let ncbd2, ct = checkCancellation runQueueId d.cancellationCheckFreq c ncbd1

        //let cbd = { progressData = pd; x = x }
        // n.logger.logDebugString $"    tryCallBack: t = {t}, d = {d}, cbd = {cbd}."

        match ct with
        | Some v ->
            notifyAll ctx (v |> CancelledCalculation |> FinalCallBack) pd t x
            let progressDataWithErr = { pd with progressData = calculateProgressDataWithErr ncbd1 t v }
            raise (ComputationAbortedException<'P> (progressDataWithErr, v))
        | None ->
            // let c, v = n.callBackInfo.needsCallBack.invoke d t
            let ncbd3, v = (needsCallBack i o).invoke ncbd2 t
            // n.logger.logDebugString $"    tryCallBack: t = {t}, setting needsCallBackData to c = {c}, v = {v}."

            match v with
            | None -> ()
            | Some v ->
                match v with
                | ProgressNotification -> notifyProgress s RegularCallBack pd
                | ChartNotification -> updateCharts ctx t x
                | ChartDetailedNotification -> notifyChartsDetailed ctx t x
                | ProgressAndChartNotification ->
                    notifyProgress s RegularCallBack pd
                    updateCharts ctx t x
                | AllNotification -> notifyAll ctx RegularCallBack pd t x

            ncbd3


    //let notifyOfCharts d t =
    //    printfn $"notifyOfCharts: t = %A{t}"
    //    let charts = d.callBackInfo.chartCallBack.generateCharts()
    //
    //    let chartResult =
    //        {
    //            runQueueId = d.runQueueId
    //            charts = charts
    //        }
    //        |> plotAllResults t
    //        |> proxy.saveCharts
    //
    //    printfn $"notifyOfResults completed with result: %A{chartResult}"
    //    chartResult


    let runSover<'D, 'P, 'X, 'C> (ctx : SolverRunnerContext<'D, 'P, 'X, 'C>) =
        let d = ctx.runnerData
        let u = ctx.userProxy
        let s = ctx.systemProxy

        let runQueueId = d.runQueueId
        let modelData = d.modelData.modelData

        let getProgressData t x =
            {
                progressData = (getNeedsCallBackData runQueueId).progressData
                progressDetailed = u.solverProxy.getProgressData |> Option.map (fun e -> e modelData t x)
            }

        let updateNeedsCallBackData t v =
            let ncbd = getNeedsCallBackData runQueueId
            let ncbd1 = tryCallBack ctx ncbd t v
            setNeedsCallBackData runQueueId ncbd1

        let tryCallBack : TryCallBack<'X> = TryCallBack updateNeedsCallBackData

        // Calculate initial progress, including additional progress data, and notify about beginning of computation.
        let t0 = d.modelData.solverInputParams.startTime
        let x0 = u.solverProxy.getInitialData modelData
        let pd0 = getProgressData t0 x0
        updateNeedsCallBackData d.modelData.solverInputParams.startTime x0
        notifyAll ctx RegularCallBack pd0 t0 x0

        let i = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue TimerEventErr (fun () -> notifyOfCharts ctx) "runSover - notifyOfCharts"
        let h = TimerEventHandler i
        do h.start()

        try
            try
                // Run the computation from the initial data till the end and report progress on the way.
                let (tEnd, xEnd) = u.solverRunner.invoke (t0, x0) tryCallBack

                // Calculate final progress, including additional progress data, and notify about completion of computation.
                let pd = getProgressData tEnd xEnd
                notifyAll ctx (FinalCallBack CompletedCalculation) pd tEnd xEnd

                //(tEnd, xEnd)
            with
            | :? ComputationAbortedException<'P> as ex ->
                let pd = ex.progressData

                match ex.cancellationType with
                | CancelWithResults e ->
                    notifyCharts ctx RegularChartGeneration
                    notifyProgress s (e |> CancelWithResults |> CancelledCalculation |> FinalCallBack) pd
                | AbortCalculation e ->
                    notifyProgress s (e |> AbortCalculation |> CancelledCalculation |> FinalCallBack) pd
            | ex ->
                let ncbd = getNeedsCallBackData d.runQueueId
                let pd = { progressData = { ncbd.progressData with errorMessageOpt = ErrorMessage $"{ex}" |> Some }; progressDetailed = None }
                notifyProgress s (Some $"{ex}" |> AbortCalculation |> CancelledCalculation |> FinalCallBack) pd
        finally
            h.stop()
