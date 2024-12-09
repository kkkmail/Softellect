namespace Softellect.DistributedProcessing.Proxy

open System
open System.IO
open System.Threading
open System.Diagnostics
open System.Management

open Softellect.Messaging.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Client
open Softellect.Messaging.Errors
open Softellect.Messaging.DataAccess
open Softellect.Sys.Errors
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
open Softellect.DistributedProcessing.Messages

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
            tryGetAvailableWorkerNode : SolverId -> DistributedProcessingResult<WorkerNodeId option>
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
    let checkRunning (r : CheckRunningRequest) : CheckRunningResult =
        Logger.logTrace $"r: %A{r}"
        try
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

            match r with
            | AnyRunning f ->
                Logger.logTrace $"f: %A{f}, processes: %A{processes}."
                let v = $"{f.value}".ToLower()

                let p =
                    processes
                    |> List.map (fun (_, e) -> e.ToLower())
                    |> List.filter (fun e -> e.StartsWith v)

                match p.Length with
                | 0 -> CanRun
                | _ -> TooManyRunning p.Length
            | RunQueueRunning (no, q) ->
                let v = $"{q.value}".ToLower()
                let pid = Process.GetCurrentProcess().Id
                Logger.logTrace $"q: %A{q}, no: %A{no}, processes: %A{processes}."

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

    type FailedSolverProxy =
        {
            tryUpdateFailedSolver : RunQueueId -> DistributedProcessingError -> DistributedProcessingResult<RetryState>
            createMessage : MessageInfo<DistributedProcessingMessageData> -> Message<DistributedProcessingMessageData>
            saveMessage : Message<DistributedProcessingMessageData> -> MessagingUnitResult
            getFailedSolverMessageInfo : RunQueueId -> string -> PartitionerMessageInfo
            deleteRunQueue : RunQueueId -> DistributedProcessingUnitResult
        }


    let private getFailedProgress q s =
        {
            runQueueId = q
            updatedRunQueueStatus = Some FailedRunQueue
            progressData =
                {
                    progressInfo =
                        {
                            progress = 0.0m
                            callCount = 0L
                            evolutionTime = EvolutionTime.defaultValue
                            relativeInvariant = RelativeInvariant.defaultValue
                            errorMessageOpt = Some (ErrorMessage s)
                        }
                    progressDetailed = None
                }
        }


    let private getFailedSolverMessageInfo partitionerId q s =
        let p = getFailedProgress q s

        {
            partitionerRecipient = partitionerId
            deliveryType = GuaranteedDelivery
            messageData = UpdateProgressPrtMsg (p.toProgressUpdateInfo())
        }


    let private onFailedSolver (proxy : FailedSolverProxy) (q : RunQueueId) e=
        Logger.logTrace $"onFailedSolver: %A{q}, error: '%A{e}'."

        let failRunQueue s =
            Logger.logTrace $"Sending a message about failed to start: %A{q}."

            let r =
                (proxy.getFailedSolverMessageInfo q s).getMessageInfo()
                |> proxy.createMessage
                |> proxy.saveMessage

            Logger.logTrace $"Message sent with result: %A{r}."

            match r with
            | Ok() ->
                Logger.logTrace $"Deleting failed: %A{r}."
                proxy.deleteRunQueue q
            | Error e1 ->
                Logger.logError $"Failed to delete %A{q} with: '%A{e1}', outer error: '%A{e}'."
                (e1 |> FailRunQueueMessagingErr |> OnFaileSolverErr) + e |> Error

        match proxy.tryUpdateFailedSolver q e with
        | Ok r ->
            match r with
            | CanRetry ->
                Logger.logTrace $"onFailedSolver: can retry for %A{q}."
                Ok()
            | ExceededRetryCount v ->
                let m = $"%A{q} exceeded retry count {v.retryCount}. Current count: {v.maxRetries}. Error %A{e}."
                Logger.logWarn m
                failRunQueue m
        | Error e1 ->
            let m = $"Error: {(e1 + e)}."
            Logger.logError m
            failRunQueue m

#endif

#if WORKER_NODE

    type TryRunSolverProcessProxy =
        {
            tryGetSolverLocation : RunQueueId -> DistributedProcessingResult<FolderName option>
            failedSolverProxy : FailedSolverProxy
        }


    let private tryGetSolverFullName folderName =
        let fileName = FileName SolverRunnerName
        fileName, fileName.tryGetFullFileName(Some folderName)


    /// Tries to run a solver with a given RunQueueId if it is not already running and if the number
    /// of running solvers is less than a given allowed max value.
    let tryRunSolverProcess o (p : TryRunSolverProcessProxy) n (q : RunQueueId) =
        Logger.logTrace $"tryRunSolverProcess: n = {n}, q = '%A{q}'."

        let elevate f = f |> TryRunSolverProcessErr
        let toError e = e |> elevate |> Error

        let onFailedSolverStart result =
            Logger.logTrace $"onFailedSolverStart: %A{q}, result: '%A{result}'."

            match result with
            | Ok r -> Ok r
            | Error e ->
                onFailedSolver p.failedSolverProxy q e |> Logger.logIfError |> ignore
                Error e

        match p.tryGetSolverLocation q with
        | Ok (Some folderName) ->
            Logger.logTrace $"tryRunSolverProcess: folderName = '{folderName}'."
            match tryGetSolverFullName folderName with
            | fileName, Ok e ->
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
                            | Error e -> (q, f, e) |> FailedToCreateOutputFolderErr |> toError

                    match ea with
                    | Ok (exeName, args) ->
                        Logger.logTrace $"tryRunSolverProcess: exeName = '{exeName}', args: '{args}'."

                        try
                            // Uncomment temporarily when testing failed solver start.
                            // Thread.Sleep(20_000)
                            // failwith $"tryRunSolverProcess: testing failed run: %A{q}."

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
                                q |> FailedToRunSolverProcessErr |> toError
                        with
                        | ex ->
                            Logger.logError $"Failed to start process: {fileName} with exception: {ex}."
                            (q, ex) |> FailedToRunSolverProcessExn |> toError
                    | Error e -> Error e

                // Decrease max value by one to account for the solver to be started.
                match checkRunning (RunQueueRunning ((Some (n - 1)), q)) with
                | CanRun ->
                    let r = run()
                    Logger.logTrace $"About to call onFailedSolverStart '%A{r}'."
                    onFailedSolverStart r
                | e ->
                    Logger.logWarn $"Can't run %A{q}: %A{e}."
                    q |> CannotRunSolverProcessErr |> toError
            | _, Error e ->
                Logger.logError $"tryRunSolverProcess: %A{q}, error: '{e}'."
                q |> FailedToLoadSolverNameErr |> toError |> onFailedSolverStart
        | Ok None -> q |> CannotLoadSolverNameErr |> toError |> onFailedSolverStart
        | Error e ->
            Logger.logError $"tryRunSolverProcess: %A{q}, error: '{e}'."
            q |> FailedToLoadSolverNameErr |> toError |> onFailedSolverStart


    let getSolverLocation (i : WorkerNodeLocalInto) (solverName : SolverName) =
        i.solverLocation.combine solverName.folderName
        //FolderName @"C:\GitHub\Softellect\Samples\DistrProc\WorkerNodeService\bin\x64\Release\net9.0"


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


    let private checkSolverRunning (i : WorkerNodeLocalInto) (solverName : SolverName) =
        let solverLocation = getSolverLocation i solverName

        match tryGetSolverFullName solverLocation with
        | _, Ok f -> checkRunning (AnyRunning f)
        | _, Error e -> InvalidOperationException $"%A{e}" :> exn |> GetProcessesByNameExn


    type WorkerNodeProxy =
        {
            saveModelData : RunQueueId -> SolverId -> ModelBinaryData -> DistributedProcessingUnitResult
            requestCancellation : RunQueueId -> CancellationType -> DistributedProcessingUnitResult
            notifyOfResults : RunQueueId -> ResultNotificationType -> DistributedProcessingUnitResult
            // loadAllActiveRunQueueId : unit -> DistributedProcessingResult<list<RunQueueId>>
            loadAllNotStartedRunQueueId : float<minute> -> DistributedProcessingResult<list<RunQueueId>>
            tryGetSolverLocation : RunQueueId -> DistributedProcessingResult<FolderName option>
            tryRunSolverProcess : TryRunSolverProcessProxy -> int -> RunQueueId -> DistributedProcessingResult<ProcessId>
            saveSolver : Solver -> DistributedProcessingUnitResult
            tryDecryptSolver : EncryptedSolver -> PartitionerId -> DistributedProcessingResult<Solver>
            unpackSolver : FolderName -> Solver -> DistributedProcessingUnitResult
            checkSolverRunning : SolverName -> CheckRunningResult
            setSolverDeployed : SolverId -> DistributedProcessingUnitResult
            createMessage : MessageInfo<DistributedProcessingMessageData> -> Message<DistributedProcessingMessageData>
            saveMessage : Message<DistributedProcessingMessageData> -> MessagingUnitResult
            loadAllNotDeployedSolverId : unit -> DistributedProcessingResult<list<SolverId>>
            tryLoadSolver : SolverId -> DistributedProcessingResult<Solver>
            tryUpdateFailedSolver : RunQueueId -> DistributedProcessingError -> DistributedProcessingResult<RetryState>
            getFailedSolverMessageInfo : RunQueueId -> string -> PartitionerMessageInfo
            deleteRunQueue : RunQueueId -> DistributedProcessingUnitResult
        }

        member p.tryRunSolverProcessProxy =
            {
                tryGetSolverLocation = p.tryGetSolverLocation
                failedSolverProxy =
                    {
                        tryUpdateFailedSolver = p.tryUpdateFailedSolver
                        createMessage = p.createMessage
                        saveMessage = p.saveMessage
                        getFailedSolverMessageInfo = p.getFailedSolverMessageInfo
                        deleteRunQueue = p.deleteRunQueue
                    }
            }

        static member create (i : WorkerNodeServiceInfo) : WorkerNodeProxy =
            {
                saveModelData = saveModelData
                requestCancellation = tryRequestCancelRunQueue
                notifyOfResults = fun q r -> tryNotifyRunQueue q (Some r)
                // loadAllActiveRunQueueId = loadAllActiveRunQueueId
                loadAllNotStartedRunQueueId = loadAllNotStartedRunQueueId
                tryGetSolverLocation = tryGetSolverLocation i.workerNodeLocalInto
                tryRunSolverProcess = tryRunSolverProcess (Some i.workerNodeLocalInto.solverOutputLocation)
                saveSolver = saveSolver
                tryDecryptSolver = tryDecryptSolver i.workerNodeInfo
                unpackSolver = unpackSolver
                checkSolverRunning = checkSolverRunning i.workerNodeLocalInto
                setSolverDeployed = setSolverDeployed
                createMessage = createMessage messagingDataVersion i.workerNodeInfo.workerNodeId.messagingClientId
                saveMessage = saveMessage<DistributedProcessingMessageData> messagingDataVersion
                loadAllNotDeployedSolverId = loadAllNotDeployedSolverId
                tryLoadSolver = tryLoadSolver
                tryUpdateFailedSolver = tryUpdateFailedSolver
                getFailedSolverMessageInfo = getFailedSolverMessageInfo i.workerNodeInfo.partitionerId
                deleteRunQueue = deleteRunQueue
            }


    type WorkerNodeRunnerContext =
        {
            workerNodeServiceInfo : WorkerNodeServiceInfo
            workerNodeProxy : WorkerNodeProxy
            messagingClientData : MessagingClientData<DistributedProcessingMessageData>
        }


    let notifyOfSolverDeployment (ctx : WorkerNodeRunnerContext) s r =
        let elevate e = e |> NotifyOfSolverDeploymentMessagingErr |> NotifyOfSolverDeploymentErr
        let toError e = e |> elevate |> Error

        let r1 =
            {
                recipientInfo =
                    {
                        recipient = ctx.workerNodeServiceInfo.workerNodeInfo.partitionerId.messagingClientId
                        deliveryType = GuaranteedDelivery
                    }

                messageData = (ctx.workerNodeServiceInfo.workerNodeInfo.workerNodeId, s, r) |> SolverDeploymentResultMsg |> PartitionerMsg |> UserMsg
            }
            |> ctx.workerNodeProxy.createMessage
            |> ctx.workerNodeProxy.saveMessage

        match r, r1 with
        | Ok (), Ok () -> Ok()
        | Error e, Ok () -> Error e
        | Ok (), Error e1 -> toError e1
        | Error e, Error e1 -> e + (elevate e1) |> Error

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
