namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.Errors

module Runner =

    let calculateProgress n t =
        (t - n.solveInputParams.startTime) / (n.solveInputParams.endTime - n.solveInputParams.startTime)
        |> decimal


    let shouldNotifyByCallCount d =
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


    let shouldNotifyByNextProgress n d t =
        let p = calculateProgress n t
        let r = p >= d.nextProgress
        // n.logger.logDebugString $"shouldNotifyByNextProgress: p = {p}, nextProgress = {d.nextProgress}, r = {r}."
        r


    let shouldNotifyByNextChartProgress n d t =
        let p = calculateProgress n t
        let r = p >= d.nextChartProgress
        // n.logger.logDebugString $"shouldNotifyByNextChartProgress: p = {p}, nextChartProgress = {d.nextChartProgress}, r = {r}."
        r


    let shouldNotifyByNextChartDetailedProgress n d t =
        // n.logger.logDebugString $"shouldNotifyByNextChartDetailedProgress: t = {t}, n.odeParams.outputParams.noOfChartDetailedPoints = {n.odeParams.outputParams.noOfChartDetailedPoints}."
        match n.solverOutputParams.noOfChartDetailedPoints with
        | Some _ ->
            let p = calculateProgress n t
            let r = p >= d.nextChartDetailedProgress
            // n.logger.logDebugString $"shouldNotifyByNextChartDetailedProgress: t = {t}, p = {p}, d.nextChartDetailedProgress = {d.nextChartDetailedProgress}, r = {r}."
            r
        | None -> false


    let calculateNextProgress n t =
        let r =
            match n.solverOutputParams.noOfProgressPoints with
            | np when np <= 0 -> 1.0m
            | np -> min 1.0m ((((calculateProgress n t) * (decimal np) |> floor) + 1.0m) / (decimal np))
        // n.logger.logDebugString $"calculateNextProgress: r = {r}."
        r


    let calculateNextChartProgress n t =
        let r =
            match n.solverOutputParams.noOfOutputPoints with
            | np when np <= 0 -> 1.0m
            | np -> min 1.0m ((((calculateProgress n t) * (decimal np) |> floor) + 1.0m) / (decimal np))
        // n.logger.logDebugString $"calculateNextChartProgress: t = {t}, r = {r}."
        r


    let calculateNextChartDetailedProgress n t =
        let r =
            match n.solverOutputParams.noOfChartDetailedPoints with
            | Some nop ->
                let r =
                    match nop with
                    | np when np <= 0 -> 1.0m
                    | np ->
                        let progress = calculateProgress n t
                        // n.logger.logDebugString $"calculateNextChartDetailedProgress: t = {t}, progress = {progress}."
                        min 1.0m ((((calculateProgress n t) * (decimal np) |> floor) + 1.0m) / (decimal np))
                r
            | None -> 1.0m
        // n.logger.logDebugString $"calculateNextChartDetailedProgress: t = {t}, r = {r}."
        r


    let shouldNotifyProgress n d t = shouldNotifyByCallCount d || shouldNotifyByNextProgress n d t
    let shouldNotifyChart n d t = shouldNotifyByCallCount d || shouldNotifyByNextChartProgress n d t


    let needsCallBack n =
        let f d t =
            let shouldNotifyProgress = shouldNotifyProgress n d t
            let shouldNotifyChart = shouldNotifyChart n d t
            let shouldNotifyChartDetailed = shouldNotifyByNextChartDetailedProgress n d t

            let nextProgress = calculateNextProgress n t
            let nextChartProgress = calculateNextChartProgress n t
            let nextChartDetailedProgress = calculateNextChartDetailedProgress n t
            // n.logger.logDebugString $"needsCallBack: t = {t}, d = {d}, shouldNotifyProgress = {shouldNotifyProgress}, shouldNotifyChart = {shouldNotifyChart}, shouldNotifyChartDetailed = {shouldNotifyChartDetailed}, nextChartDetailedProgress = {nextChartDetailedProgress}."

            let retVal =
                match (shouldNotifyProgress, shouldNotifyChart, shouldNotifyChartDetailed) with
                | false, false, false -> (d, None)
                | false, true, false ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextChartProgress to: {nextChartProgress}, ChartNotification."
                    ( { d with nextChartProgress = nextChartProgress }, Some ChartNotification)
                | true, false, false ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextProgress to: {nextProgress}, ProgressNotification."
                    ( { d with nextProgress = nextProgress }, Some ProgressNotification)
                | true, true, false ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextProgress to {nextProgress}, nextChartProgress to: {nextChartProgress}, ProgressAndChartNotification."
                    ( { d with nextProgress = nextProgress; nextChartProgress = nextChartProgress }, Some ProgressAndChartNotification)

                | false, _, true ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextChartProgress to {nextChartProgress}, nextChartDetailedProgress to: {nextChartDetailedProgress}, ChartDetailedNotification."
                    ( { d with nextChartProgress = nextChartProgress; nextChartDetailedProgress = nextChartDetailedProgress }, Some ChartDetailedNotification)
                | true, _, true ->
                    // n.logger.logDebugString $"needsCallBack: t = {t}, setting nextProgress to {nextProgress}, nextChartProgress to {nextChartProgress}, nextChartDetailedProgress to: {nextChartDetailedProgress}, AllNotification."
                    ( { d with nextProgress = nextProgress; nextChartProgress = nextChartProgress; nextChartDetailedProgress = nextChartDetailedProgress }, Some AllNotification)

            // n.logger.logDebugString $"needsCallBack: retVal = {retVal}."
            retVal

        NeedsCallBack f


    let private checkCancellation n d =
        let fromLastCheck = DateTime.Now - d.lastCheck
        // n.logger.logDebugString $"checkCancellation: runQueueId = %A{n.runQueueId}, time interval from last check = %A{fromLastCheck}."

        if fromLastCheck > n.callBackInfo.checkFreq
        then
            let cancel = n.callBackInfo.checkCancellation.invoke n.runQueueId
            { d with lastCheck = DateTime.Now}, cancel
        else d, None


    let private estCompl n t =
        match estimateEndTime (calculateProgress n t) n.started with
        | Some e -> " est. compl.: " + e.ToShortDateString() + ", " + e.ToShortTimeString() + ","
        | None -> EmptyString


    let private calculateProgressDataWithErr n d t v =
        // n.logger.logDebugString $"calculateProgressDataWithErr: Called with t = {t}, v = {v}."

        let withMessage s m =
            let eo =
                match s with
                | Some v -> $"m: '{m}', t: {t}, Message: '{v}'."
                | None -> m
                |> ErrorMessage
                |> Some

            let pd =
                {
                    progressData =
                        {
                            progress = d.progressData.progress
                            callCount = d.progressData.callCount
                            errorMessageOpt = eo
                            relativeInvariant = 1.0
                        }
                    progressDetailed = None
                }

            pd

        match v with
        | AbortCalculation s -> $"The run queue was aborted at: %.2f{d.progressData.progress * 100.0m}%% progress." |> withMessage s
        | CancelWithResults s ->
            //$"The run queue was cancelled at: %.2f{d.progressData.progressData.progress * 100.0m}%% progress. Absolute tolerance: {n.odeParams.absoluteTolerance}."
            $"The run queue was cancelled at: %.2f{d.progressData.progress * 100.0m}%% progress."
            |> withMessage s


    let private notifyAll n c d =
        n.callBackInfo.progressCallBack.invoke c d
        n.callBackInfo.chartDetailedCallBack.invoke c d


    let private tryCallBack needsCallBackData n p t x =
        let d0 = needsCallBackData
        // n.logger.logDebugString $"tryCallBack - starting: t = {t}, needsCallBackData = {d0}."
        let pd = { d0.progressData with callCount = d0.progressData.callCount + 1L; progress = calculateProgress n t }
        let d, ct = { d0 with progressData = pd } |> checkCancellation n
        let dp = p t x |> Some // Calculates detailed progress.
        let cbd = { progressData = { progressData = d.progressData; progressDetailed = dp }; t = t; x = x }
        // n.logger.logDebugString $"    tryCallBack: t = {t}, d = {d}, cbd = {cbd}."

        match ct with
        | Some v ->
            notifyAll n (v |> CancelledCalculation |> FinalCallBack) cbd
            let progressDataWithErr = calculateProgressDataWithErr n d t v
            raise(ComputationAbortedException (progressDataWithErr.progressData, v))
        | None ->
            // let c, v = n.callBackInfo.needsCallBack.invoke d t
            let c, v = (needsCallBack n).invoke d t
            // n.logger.logDebugString $"    tryCallBack: t = {t}, setting needsCallBackData to c = {c}, v = {v}."
            let newNeedsCallBackData = c

            match v with
            | None -> ()
            | Some v ->
                let i = n.callBackInfo

                match v with
                | ProgressNotification -> i.progressCallBack.invoke RegularCallBack cbd
                | ChartNotification -> i.chartCallBack.invoke RegularCallBack cbd
                | ChartDetailedNotification -> i.chartDetailedCallBack.invoke RegularCallBack cbd
                | ProgressAndChartNotification ->
                    i.progressCallBack.invoke RegularCallBack cbd
                    i.chartCallBack.invoke RegularCallBack cbd
                | AllNotification -> notifyAll n RegularCallBack cbd

            newNeedsCallBackData
