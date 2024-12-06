namespace Softellect.DistributedProcessing.Primitives

open System
open Softellect.Messaging.Primitives
open Softellect.Sys.Errors
open Softellect.Wcf.Common
open Softellect.Messaging.ServiceInfo
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.Sys.Logging
open Softellect.Sys.WindowsApi

module Common =

    [<Literal>]
    let DefaultAbsoluteTolerance = 1.0e-08


    [<Literal>]
    let SolverRunnerName = "SolverRunner.exe"


    [<Literal>]
    let SolverRunnerProcessName = "SolverRunner"


    /// The model data can be huge (100+ MB in JSON / XML), so we compress it before storing it in the database.
    /// Zipped binary carries about 100X compression ratio over not compressed JSON / XML.
    let private serializationFormat = BinaryZippedFormat


    /// The progress data should be human-readable, so that a query can be run to check the detailed progress.
    /// JSON is supported by MSSQL and so it is a good choice.
    let private progressSerializationFormat = JSonFormat

    let solverSerializationFormat = BinaryZippedFormat

    let serializeProgress = jsonSerialize
    let deserializeProgress<'T> = jsonDeserialize<'T>

    let serializeData x = serialize serializationFormat x
    let deserializeData x = deserialize serializationFormat x
    let tryDeserializeData<'A> x = tryDeserialize<'A> serializationFormat x


    let defaultNoOfOutputPoints = 1000
    let defaultNoOfProgressPoints = 100


    let defaultServicePort = 5000


    let defaultPartitionerNetTcpServicePort = defaultServicePort |> ServicePort
    let defaultPartitionerHttpServicePort = defaultPartitionerNetTcpServicePort.value + 1 |> ServicePort
    let defaultPartitionerServiceAddress = localHost |> ServiceAddress


    type PartitionerId =
        | PartitionerId of MessagingClientId

        member this.value = let (PartitionerId v) = this in v.value
        member this.messagingClientId = let (PartitionerId v) = this in v


    let defaultPartitionerId = Guid("F941F87C-BEBC-43E7-ABD3-967E377CBD57") |> MessagingClientId |> PartitionerId


    type WorkerNodeConfigParam =
        | WorkerNumberOfSores of int


    /// An encapsulation of the evolution time in the system.
    /// It is convenient to have it as a separate type to avoid confusion with other decimal values.
    type EvolutionTime =
        | EvolutionTime of decimal

        member this.value = let (EvolutionTime v) = this in v
        static member defaultValue = EvolutionTime 0.0m


    /// A relative invariant is a value that should be close to 1.0 all the time.
    type RelativeInvariant =
        | RelativeInvariant of double

        member this.value = let (RelativeInvariant v) = this in v
        static member defaultValue = RelativeInvariant 1.0


    type AbsoluteTolerance =
        | AbsoluteTolerance of double

        member this.value = let (AbsoluteTolerance v) = this in v
        static member defaultValue = AbsoluteTolerance DefaultAbsoluteTolerance


    type ResultNotificationType =
        | RegularResultGeneration // The engine will decide whether to generate the result or not.
        | ForceResultGeneration   // The  engine will generate the result.

        member n.value =
            match n with
            | RegularResultGeneration -> 1
            | ForceResultGeneration -> 2


    type CancellationType =
        | CancelWithResults of string option
        | AbortCalculation of string option

        member n.value =
            match n with
            | AbortCalculation _ -> 0
            | CancelWithResults _ -> 2


    let estimateEndTime progress (started : DateTime) =
        if progress > 0.0m && progress <= 1.0m
        then
            let estRunTime = (decimal (DateTime.Now.Subtract(started).Ticks)) / progress |> int64 |> TimeSpan.FromTicks
            started.Add estRunTime |> Some
        else None


    type ProgressInfo =
        {
            progress : decimal // Progress in the range [0.0, 1.0]
            callCount : int64
            evolutionTime : EvolutionTime // Evolution time of the system. May coincide with callCount in some cases.
            relativeInvariant : RelativeInvariant // Should be close to 1.0 all the time. Substantial deviations is a sign of errors. If not needed, then set to 1.0.
            errorMessageOpt : ErrorMessage option
        }

        static member defaultValue : ProgressInfo =
            {
                progress = 0.0m
                callCount = 0L
                evolutionTime = EvolutionTime.defaultValue
                relativeInvariant = RelativeInvariant.defaultValue
                errorMessageOpt = None
            }

        member data.estimateEndTime started = estimateEndTime data.progress started


    type ProgressData =
        {
            progressInfo : ProgressInfo
            progressDetailed : string option
        }

        static member defaultValue : ProgressData =
            {
                progressInfo = ProgressInfo.defaultValue
                progressDetailed = None
            }

        member data.estimateEndTime started = data.progressInfo.estimateEndTime started


    /// 'P is any other data that is needed for progress tracking.
    type ProgressData<'P> =
        {
            progressInfo : ProgressInfo
            progressDetailed : 'P option
        }

        static member defaultValue : ProgressData<'P> =
            {
                progressInfo = ProgressInfo.defaultValue
                progressDetailed = None
            }

        member p.toProgressData() : ProgressData =
            {
                progressInfo = p.progressInfo
                progressDetailed = p.progressDetailed |> Option.map (fun e -> serializeProgress e)
            }

    //    member data.estimateEndTime started = data.progressData.estimateEndTime started


    type WorkerNodeId =
        | WorkerNodeId of MessagingClientId

        member this.value = let (WorkerNodeId v) = this in v
        member this.messagingClientId = let (WorkerNodeId v) = this in v
        static member newId() = Guid.NewGuid() |> MessagingClientId |> WorkerNodeId


    type WorkerNodePriority =
        | WorkerNodePriority of int

        member this.value = let (WorkerNodePriority v) = this in v
        static member defaultValue = WorkerNodePriority 100


    type WorkerNodeName =
        | WorkerNodeName of string

        member this.value = let (WorkerNodeName v) = this in v
        static member newName() = $"{Guid.NewGuid()}".Replace("-", "") |> WorkerNodeName


    type SolverId =
        | SolverId of Guid

        member this.value = let (SolverId v) = this in v


    type SolverName =
        | SolverName of string

        member this.value = let (SolverName v) = this in v
        member this.folderName = this.value |> FolderName


    type SolverData =
        | SolverData of byte[]

        member this.value = let (SolverData v) = this in v


    type Solver =
        {
            solverId : SolverId
            solverName : SolverName
            description : string option
            solverData : SolverData option
        }


    type EncryptedSolver =
        | EncryptedSolver of byte[]

        member this.value = let (EncryptedSolver v) = this in v


    type SolverRunnerInfo =
        {
            runQueueId : RunQueueId
            processId : ProcessId option
        }


    type SolverInputParams =
        {
            startTime : EvolutionTime
            endTime : EvolutionTime
        }


    type SolverOutputParams =
        {
            noOfOutputPoints : int
            noOfProgressPoints : int
            noOfResultDetailedPoints : int option
        }

        static member defaultValue =
            {
                noOfOutputPoints = 2
                noOfProgressPoints = 100
                noOfResultDetailedPoints = None
            }


    type ModelBinaryData =
        {
            solverInputParams : SolverInputParams
            solverOutputParams : SolverOutputParams
            modelData : byte[]
        }


    /// Number of minutes for worker node errors to expire before the node can be again included in work distribution.
    type LastAllowedNodeErr =
        | LastAllowedNodeErr of int<minute>

        member this.value = let (LastAllowedNodeErr v) = this in v
        static member defaultValue = LastAllowedNodeErr 60<minute>


    /// Information about a worker node to be passed to partitioner.
    type WorkerNodeInfo =
        {
            workerNodeId : WorkerNodeId
            workerNodeName : WorkerNodeName
            partitionerId : PartitionerId
            noOfCores : int
            nodePriority : WorkerNodePriority
            isInactive : bool
            solverEncryptionType : EncryptionType
        }


    /// Additional worker node related information, which is not needed by partitioner.
    type WorkerNodeLocalInto =
        {
            resultLocation : FolderName
            solverLocation : FolderName
            solverOutputLocation : FolderName

            // TODO kk:20241201 - Add appsettings.json settings for messaging & other timer events.
            // x : TimerRefreshInterval
        }


    type PartitionerInfo =
        {
            partitionerId : PartitionerId
            resultLocation : FolderName
            lastAllowedNodeErr : LastAllowedNodeErr
            solverEncryptionType : EncryptionType
        }


    type ProgressUpdateInfo =
        {
            runQueueId : RunQueueId
            updatedRunQueueStatus : RunQueueStatus option
            progressData : ProgressData
        }


    type ResultInfo =
        {
            runQueueId : RunQueueId
            results : list<CalculationResult>
        }

        member r.info =
            let c = r.results|> List.map _.info |> joinStrings ", "
            $"%A{r.runQueueId}, results: {c}"


    type WorkerNodeServiceName =
        | WorkerNodeServiceName of ServiceName

        member this.value = let (WorkerNodeServiceName v) = this in v
        static member netTcpServiceName = "WorkerNodeNetTcpService" |> ServiceName |> WorkerNodeServiceName
        static member httpServiceName = "WorkerNodeHttpService" |> ServiceName |> WorkerNodeServiceName


    type WorkerNodeServiceInfo =
        {
            workerNodeInfo : WorkerNodeInfo
            workerNodeLocalInto : WorkerNodeLocalInto
            workerNodeServiceAccessInfo : ServiceAccessInfo
            messagingServiceAccessInfo : MessagingServiceAccessInfo
        }

        member this.messagingClientAccessInfo =
            {
                msgClientId = this.workerNodeInfo.workerNodeId.messagingClientId
                msgSvcAccessInfo = this.messagingServiceAccessInfo
            }


    type PartitionerServiceInfo =
        {
            partitionerInfo : PartitionerInfo
            partitionerServiceAccessInfo : ServiceAccessInfo
            messagingServiceAccessInfo : MessagingServiceAccessInfo
        }

        member this.messagingClientAccessInfo =
            {
                msgClientId = this.partitionerInfo.partitionerId.messagingClientId
                msgSvcAccessInfo = this.messagingServiceAccessInfo
            }


    type TryRunModelResult =
        | WorkScheduled
        | NoWork
        | NoAvailableWorkerNodes


    type CheckRunningRequest =
        | AnyRunning of FileName
        | RunQueueRunning of int option * RunQueueId

    type CheckRunningResult =
        | CanRun
        | AlreadyRunning of ProcessId
        | TooManyRunning of int
        | GetProcessesByNameExn of exn


    type SolverType =
        | OdeSolver
        | FredholmSolver
        | UserDefinedSolver


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


    type OdeSolverType =
        | AlgLib of AlgLibMethod
        | OdePack of OdePackMethod * CorrectorIteratorType * NegativeValuesCorrectorType

        member t.correction =
            match t with
            | AlgLib _ -> 0.0
            | OdePack (_, _, nc) -> nc.correction


    type OdeParams =
        {
            stepSize : double
            absoluteTolerance : AbsoluteTolerance
            odeSolverType : OdeSolverType
            derivative : DerivativeCalculator
        }
