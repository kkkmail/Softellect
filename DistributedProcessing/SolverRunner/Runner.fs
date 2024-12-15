namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.Errors
open System.Collections.Concurrent
open Softellect.Sys.TimerEvents

module Runner =

    let private needsCallBackDataDictionary = ConcurrentDictionary<RunQueueId, NeedsCallBackData>()


    let private getNeedsCallBackData runQueueId =
        match needsCallBackDataDictionary.TryGetValue(runQueueId) with
        | true, v -> v
        | false, _ ->
            let pid = ProcessId.getCurrentProcessId()
            let ncbd = NeedsCallBackData.defaultValue (Some pid)
            Logger.logTrace $"ncbd: %A{ncbd}."
            ncbd


    let private setNeedsCallBackData runQueueId data =
        needsCallBackDataDictionary.AddOrUpdate(runQueueId, data, (fun _ _ -> data)) |> ignore

    // ================================================================ //

    let private calculateProgress (i : SolverInputParams) (t : EvolutionTime) =
        (t.value - i.startTime.value) / (i.endTime.value - i.startTime.value)
        |> decimal


    let private shouldNotifyByCallCount (ncbd : NeedsCallBackData) =
        let callCount = ncbd.progressData.progressInfo.callCount

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


    let private shouldNotifyByNextResultProgress i ncbd t =
        let p = calculateProgress i t
        let r = p >= ncbd.nextResultProgress
        // n.logger.logDebugString $"shouldNotifyByNextResultProgress: p = {p}, nextResultProgress = {d.nextResultProgress}, r = {r}."
        r


    let private shouldNotifyByNextResultDetailedProgress i o ncbd t =
        // n.logger.logDebugString $"shouldNotifyByNextResultDetailedProgress: t = {t}, n.odeParams.outputParams.noOfResultDetailedPoints = {n.odeParams.outputParams.noOfResultDetailedPoints}."
        match o.noOfResultDetailedPoints with
        | Some _ ->
            let p = calculateProgress i t
            let r = p >= ncbd.nextResultDetailedProgress
            // n.logger.logDebugString $"shouldNotifyByNextResultDetailedProgress: t = {t}, p = {p}, d.nextResultDetailedProgress = {d.nextResultDetailedProgress}, r = {r}."
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


    let private calculateNextResultProgress i o t =
        let r = calculateNextProgressImpl i t o.noOfOutputPoints
        // n.logger.logDebugString $"calculateNextResultProgress: t = {t}, r = {r}."
        r


    let private calculateNextResultDetailedProgress i o t =
        let r =
            match o.noOfResultDetailedPoints with
            | Some nop -> calculateNextProgressImpl i t nop
            | None -> 1.0m
        // n.logger.logDebugString $"calculateNextResultDetailedProgress: t = {t}, r = {r}."
        r


    let private shouldNotifyProgress i ncbd t = shouldNotifyByCallCount ncbd || shouldNotifyByNextProgress i ncbd t
    let private shouldNotifyResult i ncbd t = shouldNotifyByCallCount ncbd || shouldNotifyByNextResultProgress i ncbd t


    let private needsCallBack i o =
        let f ncbd t =
            let shouldNotifyProgress = shouldNotifyProgress i ncbd t
            let shouldNotifyResult = shouldNotifyResult i ncbd t
            let shouldNotifyResultDetailed = shouldNotifyByNextResultDetailedProgress i o ncbd t

            let nextProgress = calculateNextProgress  i o t
            let nextResultProgress = calculateNextResultProgress i o t
            let nextResultDetailedProgress = calculateNextResultDetailedProgress i o t
            // n.logger.logDebugString $"needsCallBack: t = {t}, d = {d}, shouldNotifyProgress = {shouldNotifyProgress}, shouldNotifyResult = {shouldNotifyResult}, shouldNotifyResultDetailed = {shouldNotifyResultDetailed}, nextResultDetailedProgress = {nextResultDetailedProgress}."

            let retVal =
                match (shouldNotifyProgress, shouldNotifyResult, shouldNotifyResultDetailed) with
                | false, false, false -> (ncbd, None)
                | false, true, false ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextResultProgress to: {nextResultProgress}, ResultNotification."
                    ( { ncbd with nextResultProgress = nextResultProgress }, Some ResultNotification)
                | true, false, false ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextProgress to: {nextProgress}, ProgressNotification."
                    ( { ncbd with nextProgress = nextProgress }, Some ProgressNotification)
                | true, true, false ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextProgress to {nextProgress}, nextResultProgress to: {nextResultProgress}, ProgressAndResultNotification."
                    ( { ncbd with nextProgress = nextProgress; nextResultProgress = nextResultProgress }, Some ProgressAndResultNotification)

                | false, _, true ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextResultProgress to {nextResultProgress}, nextResultDetailedProgress to: {nextResultDetailedProgress}, ResultDetailedNotification."
                    ( { ncbd with nextResultProgress = nextResultProgress; nextResultDetailedProgress = nextResultDetailedProgress }, Some ResultDetailedNotification)
                | true, _, true ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextProgress to {nextProgress}, nextResultProgress to {nextResultProgress}, nextResultDetailedProgress to: {nextResultDetailedProgress}, AllNotification."
                    ( { ncbd with nextProgress = nextProgress; nextResultProgress = nextResultProgress; nextResultDetailedProgress = nextResultDetailedProgress }, Some AllNotification)

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


    let private calculateProgressDataWithErr (ncbd : NeedsCallBackData) (t : EvolutionTime) v =
        // n.logger.logDebugString $"calculateProgressDataWithErr: Called with t = {t}, v = {v}."

        let withMessage s m =
            let eo =
                match s with
                | Some v -> $"m: '{m}', t: {t}, Message: '{v}'."
                | None -> m
                |> ErrorMessage
                |> Some

            let pd = { ncbd.progressData.progressInfo with errorMessageOpt = eo}
            pd

        match v with
        | AbortCalculation s -> $"The run queue was aborted at: %.2f{ncbd.progressData.progressInfo.progress * 100.0m}%% progress." |> withMessage s
        | CancelWithResults s ->
            //$"The run queue was cancelled at: %.2f{d.progressData.progressData.progress * 100.0m}%% progress. Absolute tolerance: {n.odeParams.absoluteTolerance}."
            $"The run queue was cancelled at: %.2f{ncbd.progressData.progressInfo.progress * 100.0m}%% progress."
            |> withMessage s


    let private notifyProgress s cb pd =
        Logger.logTrace $"notifyProgress: cb = %A{cb}, pd = %A{pd}."
        s.callBackProxy.progressCallBack.invoke cb pd


    let private updateResults ctx t x =
        let u = ctx.userProxy
        let s = ctx.systemProxy

        let modelData = ctx.runnerData.modelData.modelData

        let cd =
            {
                resultData = u.resultGenerator.getResultData modelData t x
                t = t.value
            }

        s.addResultData cd


    let private notifyResultsDetailed ctx t x =
        let u = ctx.userProxy
        let s = ctx.systemProxy

        let modelData = ctx.runnerData.modelData.modelData
        match u.resultGenerator.generateDetailedResults ctx.runnerData.runnerData.runQueueId modelData t x with
        | Some c ->
            Logger.logTrace $"notifyResultsDetailed: c.Length = %A{c.Length}"
            s.callBackProxy.resultCallBack.invoke c
        | None ->
            Logger.logTrace $"notifyResultsDetailed: No results to generate."
            ()


    let private notifyAll ctx cb pd t x =
        let s = ctx.systemProxy
        notifyProgress s cb pd
        updateResults ctx t x
        notifyResultsDetailed ctx t x


    let private notifyResults ctx t =
        Logger.logTrace $"notifyResults: t = %A{t}"

        let u = ctx.userProxy
        let s = ctx.systemProxy

        let modelData = ctx.runnerData.modelData.modelData
        let cd = s.getResultData()

        match u.resultGenerator.generateResults ctx.runnerData.runnerData.runQueueId modelData t cd with
        | Some c ->
            Logger.logInfo $"notifyResults: c.Length = %A{c.Length}"
            s.callBackProxy.resultCallBack.invoke c
        | None ->
            Logger.logInfo $"notifyResults: No results to generate."
            ()


    /// Sends "on-request" results to the user.
    let private notifyRequestedResults ctx =
        Logger.logTrace $"notifyRequestedResults: Starting."
        let s = ctx.systemProxy
        let runQueueId = ctx.runnerData.runnerData.runQueueId

        match s.checkNotification runQueueId with
        | Some t ->
            // TODO kk:20240926 - handle errors.
            let r1 = notifyResults ctx t
            let r2 = s.clearNotification runQueueId
            // let r = combineUnitResults (DistributedProcessingError.addError) r1 r2
            Logger.logInfo $"notifyRequestedResults: r1 = %A{r1}, r2 = %A{r2}"
            Ok()
        | None ->
            Logger.logTrace $"notifyRequestedResults: No notification to process."
            Ok()


    let private tryCallBack ctx (ncbd : NeedsCallBackData) (t : EvolutionTime) x =
        // Logger.logTrace $"tryCallBack: t = %A{t}, x = %A{x}"
        let d = ctx.runnerData
        let u = ctx.userProxy
        let s = ctx.systemProxy

        let runQueueId = d.runnerData.runQueueId
        let modelData = d.modelData.modelData
        let i = d.modelData.solverInputParams
        let o = d.modelData.solverOutputParams
        let c = s.callBackProxy.checkCancellation

        let progressDetailed = u.solverProxy.getProgressData |> Option.map (fun e -> e modelData t x) // Calculates detailed progress.

        let pd =
            {
                progressInfo =
                    {
                        progress = calculateProgress i t
                        callCount = ncbd.progressData.progressInfo.callCount + 1L
                        processId = ncbd.progressData.progressInfo.processId
                        evolutionTime = t
                        relativeInvariant = u.solverProxy.getInvariant modelData t x
                        errorMessageOpt = None
                    }
                progressDetailed = progressDetailed
            }

        let ncbd1 = { ncbd with progressData = pd.toProgressData() }
        let ncbd2, ct = checkCancellation runQueueId d.runnerData.cancellationCheckFreq c ncbd1

        match ct with
        | Some v ->
            notifyAll ctx (v |> CancelledCalculation |> FinalCallBack) pd t x
            let progressDataWithErr = { pd with progressInfo = calculateProgressDataWithErr ncbd1 t v }
            raise (ComputationAbortedException<'P> (progressDataWithErr, v))
        | None ->
            let ncbd3, v = (needsCallBack i o).invoke ncbd2 t

            match v with
            | None -> ()
            | Some v ->
                match v with
                | ProgressNotification -> notifyProgress s RegularCallBack pd
                | ResultNotification -> updateResults ctx t x
                | ResultDetailedNotification -> notifyResultsDetailed ctx t x
                | ProgressAndResultNotification ->
                    notifyProgress s RegularCallBack pd
                    updateResults ctx t x
                | AllNotification -> notifyAll ctx RegularCallBack pd t x

            Logger.logTrace $"ncbd3: %A{ncbd3}."
            ncbd3


    let runSolver<'D, 'P, 'X, 'C> (ctx : SolverRunnerContext<'D, 'P, 'X, 'C>) =
        let d = ctx.runnerData
        let u = ctx.userProxy
        let s = ctx.systemProxy

        let runQueueId = d.runnerData.runQueueId
        let modelData = d.modelData.modelData
        Logger.logInfo $"runSolver: Starting runQueueId: '%A{runQueueId}', modelData.GetType().Name = '%A{modelData.GetType().Name}', modelData = '%A{modelData}'."

        let getProgressData t x =
            {
                progressInfo = (getNeedsCallBackData runQueueId).progressData.progressInfo
                progressDetailed = u.solverProxy.getProgressData |> Option.map (fun e -> e modelData t x)
            }

        // let getProgressUpdateInfo s p =
        //     {
        //         runQueueId = d.runQueueId
        //         updatedRunQueueStatus = s
        //         progressData = p
        //     }

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

        let i = TimerEventHandlerInfo<DistributedProcessingError>.defaultValue TimerEventErr (fun () -> notifyRequestedResults ctx) "runSolver - notifyRequestedResults"
        let h = TimerEventHandler i
        do h.start()

        try
            try
                // Run the computation from the initial data till the end and report progress on the way.
                Logger.logTrace "runSolver: Calling solverRunner.invoke"
                let tEnd, xEnd = u.solverRunner.invoke (t0, x0) tryCallBack
                Logger.logTrace "runSolver: Call to solverRunner.invoke has completed."

                // Calculate final progress, including additional progress data, and notify about completion of computation.
                let pd = getProgressData tEnd xEnd
                notifyAll ctx (FinalCallBack CompletedCalculation) pd tEnd xEnd
                notifyResults ctx RegularResultGeneration
                Logger.logTrace "runSolver: Final results have been sent."
            with
            | :? ComputationAbortedException<'P> as ex ->
                Logger.logInfo $"runSolver: ComputationAbortedException: %A{ex}."
                let pd = ex.progressData

                match ex.cancellationType with
                | CancelWithResults e ->
                    Logger.logInfo $"runSolver: Calculation was cancelled with e = %A{e}"
                    notifyResults ctx RegularResultGeneration
                    notifyProgress s (e |> CancelWithResults |> CancelledCalculation |> FinalCallBack) pd
                | AbortCalculation e ->
                    Logger.logInfo $"runSolver: Calculation was aborted with e = %A{e}"
                    notifyProgress s (e |> AbortCalculation |> CancelledCalculation |> FinalCallBack) pd
            | ex ->
                Logger.logError $"runSolver: Exception: %A{ex}"
                let ncbd = getNeedsCallBackData d.runnerData.runQueueId
                let pd = { progressInfo = { ncbd.progressData.progressInfo with errorMessageOpt = ErrorMessage $"%A{ex}" |> Some }; progressDetailed = None }
                notifyProgress s (Some $"%A{ex}" |> AbortCalculation |> CancelledCalculation |> FinalCallBack) pd
        finally
            Logger.logTrace $"runSolver: Stopping timers."
            h.stop()
