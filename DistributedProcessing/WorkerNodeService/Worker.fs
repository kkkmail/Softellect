namespace Softellect.DistributedProcessing.WorkerNodeService

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Softellect.DistributedProcessing.AppSettings.WorkerNodeService
open Softellect.DistributedProcessing.Errors
open Softellect.Sys.Logging
open Softellect.Wcf.Service
open CoreWCF
open Softellect.DistributedProcessing.Primitives.Common

module Worker =

    type IWorkerNodeService =
        inherit IHostedService
        /// To check if the service is working.
        abstract ping : unit -> DistributedProcessingUnitResult


    /// https://gist.github.com/dgfitch/661656
    [<ServiceContract(ConfigurationName = WorkerNodeWcfServiceName)>]
    type IWorkerNodeWcfService =
        [<OperationContract(Name = "ping")>]
        abstract ping : q:byte[] -> byte[]


    type WorkerNodeWcfService(w : IWorkerNodeService) =

        let toPingError f = f |> PingWcfErr |> WorkerNodeWcfErr

        let ping() = w.ping()

        interface IWorkerNodeWcfService with
            member _.ping b = tryReply ping toPingError b


    type WorkerNodeService(w : WorkerNodeServiceInfo) =

        let ping() = failwith $"Not implemented yet - %A{w}"

        interface IWorkerNodeService with
            member _.ping() = ping()

        interface IHostedService with
            member _.StartAsync(_ : CancellationToken) =
                async {
                    Logger.logInfo "WorkerNodeService::StartAsync..."
                }
                |> Async.StartAsTask
                :> Task

            member _.StopAsync(_ : CancellationToken) =
                async {
                    Logger.logInfo "WorkerNodeService::StopAsync..."
                }
                |> Async.StartAsTask
                :> Task
