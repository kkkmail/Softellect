namespace Softellect.DistributedProcessing.Proxy

open System
open System.Threading
open System.Diagnostics
open System.Management

open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Rop
open Softellect.Sys.TimerEvents

open Softellect.Wcf.Common
open Softellect.Wcf.Client

open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy
open System
open FSharp.Data.Sql
open Softellect.Sys.Core
open Softellect.Sys.Primitives
open Softellect.Sys.Retry
open Softellect.Sys.DataAccess
open Softellect.DistributedProcessing.VersionInfo
open Softellect.Sys.AppSettings
open Softellect.Messaging.Client
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.Primitives

#if WORKER_NODE
open Softellect.DistributedProcessing.WorkerNodeService.Primitives
open Softellect.DistributedProcessing.DataAccess.WorkerNodeService
#endif

// ==========================================

#if PARTITIONER
#endif

#if SOLVER_RUNNER || WORKER_NODE
#endif

#if SOLVER_RUNNER
#endif

#if WORKER_NODE
#endif

// ==========================================

#if PARTITIONER
module PartitionerService =
#endif

#if SOLVER_RUNNER
module SolverRunner =
#endif

#if WORKER_NODE
module WorkerNodeService =
#endif

// ==========================================

    // To make a compiler happy.
    let private dummy = 0

// ==========================================

#if PARTITIONER
#endif

#if SOLVER_RUNNER || WORKER_NODE
    type SendMessageProxy<'D, 'P> =
        {
            partitionerId : PartitionerId
            sendMessage : DistributedProcessingMessageInfo<'D, 'P> -> MessagingUnitResult
        }


    type OnUpdateProgressProxy<'D, 'P> =
        {
            // Was called tryDeleteWorkerNodeRunModelData.
            tryDeleteRunQueue : unit -> DistributedProcessingUnitResult
            tryUpdateProgressData : ProgressData<'P> -> DistributedProcessingUnitResult
            sendMessageProxy : SendMessageProxy<'D, 'P>
        }


    /// Returns CanRun when a given RunQueueId is NOT used by any of the running solvers
    /// except the current one and when a number of running solvers is less than a maximum allowed value.
    ///
    /// See:
    ///     https://stackoverflow.com/questions/504208/how-to-read-command-line-arguments-of-another-process-in-c
    ///     https://docs.microsoft.com/en-us/dotnet/core/porting/windows-compat-pack
    ///     https://stackoverflow.com/questions/33635852/how-do-i-convert-a-weakly-typed-icollection-into-an-f-list
    let checkRunning no (RunQueueId q) : CheckRunningResult =
        try
            let v = $"{q}".ToLower()
            let pid = Process.GetCurrentProcess().Id

            let wmiQuery = $"select Handle, CommandLine from Win32_Process where Caption = '{SolverRunnerName}'"
            let searcher = new ManagementObjectSearcher(wmiQuery)
            let retObjectCollection = searcher.Get()

            let processes =
                retObjectCollection
                |> Seq.cast
                |> List.ofSeq
                |> List.map (fun e -> e :> ManagementObject)
                |> List.map (fun e -> e.["Handle"], e.["CommandLine"])
                |> List.map (fun (a, b) -> int $"{a}", $"{b}")

            let run() =
                let p =
                    processes
                    |> List.map (fun (i, e) -> i, e.ToLower().Contains(v) && i <> pid)
                    |> List.tryFind snd

                match p with
                | None -> CanRun
                | Some (i, _) -> i |> ProcessId |> AlreadyRunning

            match no with
            | Some n ->
                match processes.Length <= n with
                | true -> run()
                | false -> TooManyRunning processes.Length
            | None -> run()
        with
        | e -> e |> GetProcessesByNameExn

#endif

#if SOLVER_RUNNER

    type SolverUpdateProxy<'P> =
        {
            updateProgress : ProgressUpdateInfo<'P> -> DistributedProcessingUnitResult
//            updateTime : ProgressData -> UnitResult
            checkCancellation : RunQueueId -> CancellationType option
            logCrit : SolverRunnerCriticalError -> DistributedProcessingUnitResult
        }


    type SolverNotificationProxy =
        {
            checkNotificationRequest : RunQueueId -> ResultNotificationType option
            clearNotificationRequest : RunQueueId -> DistributedProcessingUnitResult
        }


    type SolverRunnerProxy<'P> =
        {
            solverUpdateProxy : SolverUpdateProxy<'P>
            solverNotificationProxy : SolverNotificationProxy
//            saveResult : ResultDataWithId -> UnitResult
            saveCharts : ChartGenerationResult -> DistributedProcessingUnitResult
            logCrit : SolverRunnerCriticalError -> DistributedProcessingUnitResult
        }

#endif

#if WORKER_NODE
    type OnProcessMessageProxy<'D> =
        {
            saveModelData : RunQueueId -> 'D -> DistributedProcessingUnitResult
            requestCancellation : RunQueueId -> CancellationType -> DistributedProcessingUnitResult
            notifyOfResults : RunQueueId -> ResultNotificationType -> DistributedProcessingUnitResult
            onRunModel : RunQueueId -> DistributedProcessingUnitResult
        }


    type WorkerNodeProxy<'D> =
        {
            onProcessMessageProxy : OnProcessMessageProxy<'D>
            loadAllActiveRunQueueId : unit -> DistributedProcessingResult<list<RunQueueId>>
            //logCrit : SolverRunnerCriticalError -> UnitResult
        }

        static member create sr : WorkerNodeProxy<'D> =
            {
                onProcessMessageProxy =
                    {
                        saveModelData = saveModelData
                        requestCancellation = tryRequestCancelRunQueue
                        notifyOfResults = fun q r -> tryNotifyRunQueue q (Some r)
                        onRunModel = sr
                    }

                loadAllActiveRunQueueId = loadAllActiveRunQueueId
                //logCrit = saveSolverRunnerErrFs name
            }


    type WorkerNodeRunnerData<'D, 'P> =
        {
            workerNodeServiceInfo : WorkerNodeServiceInfo
            workerNodeProxy : WorkerNodeProxy<'D>
            //messageProcessorProxy : DistributedProcessingMessageProcessorProxy<'D, 'P>
            //messageProcessor : IMessageProcessor<DistributedProcessingMessageData<'D, 'P>>
            messagingClientData : MessagingClientData<DistributedProcessingMessageData<'D, 'P>>
            tryRunSolverProcess : int -> RunQueueId -> DistributedProcessingUnitResult
        }


    /// Tries to run a solver with a given RunQueueId if it not already running and if the number
    /// of running solvers is less than a given allowed max value.
    let tryRunSolverProcess n (RunQueueId q) =
        let fileName = SolverRunnerName

        let run() =
            // TODO kk:20210511 - Build command line using Argu.
            let args = $"q {q}"

            try
                let procStartInfo =
                    ProcessStartInfo(
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
                        UseShellExecute = true,
                        FileName = getExeName fileName,
                        Arguments = args
                    )

                procStartInfo.WorkingDirectory <- getAssemblyLocation()
                procStartInfo.WindowStyle <- ProcessWindowStyle.Hidden
                let p = new Process(StartInfo = procStartInfo)
                let started = p.Start()

                if started
                then
                    p.PriorityClass <- ProcessPriorityClass.Idle
                    let processId = p.Id |> ProcessId
                    printfn $"Started: {p.ProcessName} with pid: {processId}."
                    Some processId
                else
                    printfn $"Failed to start process: {fileName}."
                    None
            with
            | ex ->
                printfn $"Failed to start process: {fileName} with exception: {ex}."
                None

        // Decrease max value by one to account for the solver to be started.
        match checkRunning (Some (n - 1)) (RunQueueId q) with
        | CanRun -> run()
        | e ->
            printfn $"Can't run run queue with id {q}: %A{e}."
            None


    //type OnRegisterProxy<'D, 'P> =
    //    {
    //        workerNodeInfo : WorkerNodeInfo
    //        sendMessageProxy : SendMessageProxy<'D, 'P>
    //    }


    //type OnStartProxy =
    //    {
    //        loadAllActiveRunQueueId : unit -> DistributedProcessingResult<list<RunQueueId>>
    //        onRunModel : RunQueueId -> DistributedProcessingUnitResult
    //    }
#endif
