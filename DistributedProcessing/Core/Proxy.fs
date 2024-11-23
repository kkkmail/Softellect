namespace Softellect.DistributedProcessing.Proxy

open System
open System.IO
open System.Threading
open System.Diagnostics
open System.Management

open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Logging
open Softellect.Sys.Rop
open Softellect.Sys.Crypto
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
open Softellect.DistributedProcessing.Primitives.Common

// ==========================================
// Blank #if template blocks

#if PARTITIONER
#endif

#if MODEL_GENERATOR
#endif

#if PARTITIONER || MODEL_GENERATOR
#endif

#if SOLVER_RUNNER || WORKER_NODE
#endif

#if SOLVER_RUNNER
#endif

#if WORKERNODE_ADM
#endif

#if WORKER_NODE
#endif

#if PARTITIONER || PARTITIONER_ADM || MODEL_GENERATOR || SOLVER_RUNNER || WORKERNODE_ADM || WORKER_NODE
#endif

// ==========================================
// Open declarations

#if PARTITIONER
open Softellect.DistributedProcessing.Primitives.PartitionerService
open Softellect.DistributedProcessing.DataAccess.PartitionerService
#endif

#if MODEL_GENERATOR
open Softellect.DistributedProcessing.DataAccess.ModelGenerator
#endif

#if WORKER_NODE
open Softellect.DistributedProcessing.Primitives.WorkerNodeService
open Softellect.DistributedProcessing.DataAccess.WorkerNodeService
#endif

// ==========================================
// Module declarations

#if PARTITIONER
module PartitionerService =
#endif

#if PARTITIONER_ADM
module PartitionerAdm =
#endif

#if MODEL_GENERATOR
module ModelGenerator =
#endif

#if SOLVER_RUNNER
module SolverRunner =
#endif

#if WORKERNODE_ADM
module WorkerNodeAdm =
#endif

#if WORKER_NODE
module WorkerNodeService =
#endif

// ==========================================
// To make a compiler happy.

#if PARTITIONER || PARTITIONER_ADM || MODEL_GENERATOR || SOLVER_RUNNER || WORKERNODE_ADM || WORKER_NODE
    let private dummy = 0
#endif

// ==========================================
// Code

#if PARTITIONER

    type PartitionerProxy =
        {
            saveResults : ResultInfo -> DistributedProcessingUnitResult
            loadModelBinaryData : RunQueueId -> DistributedProcessingResult<ModelBinaryData>
            loadWorkerNodeInfo : WorkerNodeId -> DistributedProcessingResult<WorkerNodeInfo>
            tryLoadFirstRunQueue : unit -> DistributedProcessingResult<RunQueue option>
            tryGetAvailableWorkerNode : unit -> DistributedProcessingResult<WorkerNodeId option>
            upsertRunQueue : RunQueue -> DistributedProcessingUnitResult
            tryLoadRunQueue : RunQueueId -> DistributedProcessingResult<RunQueue option>
            upsertWorkerNodeInfo : WorkerNodeInfo -> DistributedProcessingUnitResult
        }


    type PartitionerContext =
        {
            partitionerServiceInfo : PartitionerServiceInfo
            partitionerProxy : PartitionerProxy
            messagingClientData : MessagingClientData<DistributedProcessingMessageData>
        }

#endif

#if SOLVER_RUNNER || WORKER_NODE

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

#if WORKER_NODE

    /// Tries to run a solver with a given RunQueueId if it is not already running and if the number
    /// of running solvers is less than a given allowed max value.
    let tryRunSolverProcess tryGetSolverLocation o n (q : RunQueueId) =
        Logger.logTrace $"tryRunSolverProcess: n = {n}, q = '%A{q}'."

        let fileName = FileName SolverRunnerName
        let elevate f = f |> TryRunSolverProcessErr |> Error

        match tryGetSolverLocation q with
        | Ok (Some folderName) ->
            Logger.logTrace $"tryRunSolverProcess: folderName = '{folderName}'."
            match fileName.tryGetFullFileName(Some folderName) with
            | Ok e ->
                let run() =
                    let ea =
                        match o with
                        | None -> (e.value, $"q {q.value}") |> Ok
                        | Some f ->
                            let outputFile = (FileName $"-s__{q.value}.txt").combine f
                            match f.tryEnsureFolderExists() with
                            | Ok() ->
                                let a = $"/c {e.value} q {q.value} > {outputFile.value} 2>&1 3>&1 4>&1 5>&1 6>&1"
                                ("cmd.exe", a) |> Ok
                            | Error e -> (q, f, e) |> FailedToCreateOutputFolderErr |> elevate

                    match ea with
                    | Ok (exeName, args) ->
                        Logger.logTrace $"tryRunSolverProcess: exeName = '{exeName}', args: '{args}'."

                        try
                            let procStartInfo =
                                ProcessStartInfo(
                                    RedirectStandardOutput = false,
                                    RedirectStandardError = false,
                                    UseShellExecute = true,
                                    FileName = exeName,
                                    Arguments = args
                                )

                            procStartInfo.WorkingDirectory <- folderName.value
                            //procStartInfo.WindowStyle <- ProcessWindowStyle.Hidden
                            procStartInfo.WindowStyle <- ProcessWindowStyle.Normal
                            let p = new Process(StartInfo = procStartInfo)
                            let started = p.Start()

                            if started
                            then
                                p.PriorityClass <- ProcessPriorityClass.Idle
                                let processId = p.Id |> ProcessId
                                Logger.logTrace $"Started: {p.ProcessName} with pid: {processId}."
                                Ok processId
                            else
                                Logger.logError $"Failed to start process: {fileName}."
                                q |> FailedToRunSolverProcessErr |> elevate
                        with
                        | ex ->
                            Logger.logError $"Failed to start process: {fileName} with exception: {ex}."
                            (q, ex) |> FailedToRunSolverProcessWithExErr |> elevate
                    | Error e -> Error e

                // Decrease max value by one to account for the solver to be started.
                match checkRunning (Some (n - 1)) q with
                | CanRun -> run()
                | e ->
                    Logger.logWarn $"Can't run run queue with id %A{q}: %A{e}."
                    q |> CannotRunSolverProcessErr |> elevate
            | Error e ->
                Logger.logError $"tryRunSolverProcess: %A{q}, error: '{e}'."
                q |> FailedToLoadSolverNameErr |> elevate
        | Ok None -> q |> CannotLoadSolverNameErr |> elevate
        | Error e ->
            Logger.logError $"tryRunSolverProcess: %A{q}, error: '{e}'."
            q |> FailedToLoadSolverNameErr |> elevate


    let getSolverLocation (i : WorkerNodeLocalInto) (solverName : SolverName) =
        i.solverLocation.combine solverName.folderName
        //FolderName @"C:\GitHub\Softellect\Samples\DistrProc\WorkerNodeService\bin\x64\Debug\net8.0"


    let private tryGetSolverLocation (i : WorkerNodeLocalInto) q =
        match tryGetSolverName q with
        | Ok (Some s) -> getSolverLocation i s |> Some |> Ok
        | Ok None -> Ok None
        | Error e -> Error e


    let private tryDecryptSolver (w : WorkerNodeInfo) (EncryptedSolver e) (p : PartitionerId) : DistributedProcessingResult<Solver> =
        match tryLoadWorkerNodePrivateKey (), tryLoadPartitionerPublicKey () with
        | Ok (Some w1), Ok (Some p1) ->
            match tryDecryptAndVerify w.solverEncryptionType e w1 p1 with
            | Ok data ->
                match tryDeserialize<Solver> solverSerializationFormat data with
                | Ok solver -> Ok solver
                | Error e -> e |> TryDecryptSolverSerializationErr |> TryDecryptSolverErr |> Error
            | Error e -> e |> TryDecryptSolverSysErr |> TryDecryptSolverErr |> Error
        | _ -> p |> TryDecryptSolverCriticalErr |> TryDecryptSolverErr |> Error


    type WorkerNodeProxy =
        {
            saveModelData : RunQueueId -> SolverId -> ModelBinaryData -> DistributedProcessingUnitResult
            requestCancellation : RunQueueId -> CancellationType -> DistributedProcessingUnitResult
            notifyOfResults : RunQueueId -> ResultNotificationType -> DistributedProcessingUnitResult
            loadAllActiveRunQueueId : unit -> DistributedProcessingResult<list<RunQueueId>>
            tryRunSolverProcess : int -> RunQueueId -> DistributedProcessingResult<ProcessId>
            saveSolver : Solver -> DistributedProcessingUnitResult
            tryDecryptSolver : EncryptedSolver -> PartitionerId -> DistributedProcessingResult<Solver>
            unpackSolver : FolderName -> Solver -> DistributedProcessingUnitResult
            setSolverDeployed : SolverId -> DistributedProcessingUnitResult
            loadAllNotDeployedSolverId : unit -> DistributedProcessingResult<list<SolverId>>
        }

        static member create (i : WorkerNodeServiceInfo) : WorkerNodeProxy =
            {
                saveModelData = saveModelData
                requestCancellation = tryRequestCancelRunQueue
                notifyOfResults = fun q r -> tryNotifyRunQueue q (Some r)
                loadAllActiveRunQueueId = loadAllActiveRunQueueId
                tryRunSolverProcess = tryRunSolverProcess (tryGetSolverLocation i.workerNodeLocalInto) (Some i.workerNodeLocalInto.solverOutputLocation)
                saveSolver = saveSolver
                tryDecryptSolver = tryDecryptSolver i.workerNodeInfo
                unpackSolver = unpackSolver
                setSolverDeployed = setSolverDeployed
                loadAllNotDeployedSolverId = loadAllNotDeployedSolverId
            }


    type WorkerNodeRunnerContext =
        {
            workerNodeServiceInfo : WorkerNodeServiceInfo
            workerNodeProxy : WorkerNodeProxy
            messagingClientData : MessagingClientData<DistributedProcessingMessageData>
        }

#endif

#if MODEL_GENERATOR

    /// 'I is "data" (no functions), 'D is a context (a mist of data and functions).
    type ModelGeneratorUserProxy<'I, 'D> =
        {
            /// Generates "input" parameters out of command line arguments and other parameters.
            /// The caller is responsible for baking everything in.
            /// This function could be slow for huge models and that's the reason of passing it as function rather than data.
            /// The resulting data can be huge.
            getInitialData : unit -> 'I

            /// Generates model data out of "input" parameters. This can be huge.
            generateModelData : 'I -> 'D

            getSolverInputParams : 'I -> SolverInputParams
            getSolverOutputParams : 'I -> SolverOutputParams
        }

    type ModelGeneratorSystemProxy =
        {
            saveModelData : RunQueueId -> SolverId -> ModelBinaryData -> DistributedProcessingUnitResult
        }

        static member create() : ModelGeneratorSystemProxy =
            {
                saveModelData = saveModelData
            }


    type  ModelGeneratorContext<'I, 'D> =
        {
            userProxy : ModelGeneratorUserProxy<'I, 'D>
            systemProxy : ModelGeneratorSystemProxy
            solverId : SolverId
        }

#endif
