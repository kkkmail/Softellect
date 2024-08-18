namespace Softellect.DistributedProcessing.WorkerNodeService

open System
open Argu
open System.Threading
open System.Threading.Tasks
open System.ServiceModel
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open Softellect.Sys
open Softellect.Sys.Primitives
open Softellect.Messaging.Primitives
open Softellect.Sys.AppSettings
open Softellect.Wcf.Common
open Softellect.Wcf.Client
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.AppSettings
open Softellect.Sys.AppSettings
open Softellect.Wcf.AppSettings

open Softellect.Sys.Primitives
open Softellect.Messaging.Primitives
open Softellect.Messaging.AppSettings
open Softellect.Messaging.Errors
open Softellect.Sys.AppSettings
open Softellect.Wcf.Common

open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.AppSettings
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.ServiceInfo
open Softellect.DistributedProcessing.WorkerNode
open Softellect.Sys.Worker
open Softellect.Sys
open Softellect.Wcf.Common
open Softellect.Wcf.Service

module Worker =
    let x = 1


    /// https://gist.github.com/dgfitch/661656
    [<ServiceContract(ConfigurationName = WorkerNodeWcfServiceName)>]
    type IWorkerNodeWcfService =

//        [<OperationContract(Name = "configure")>]
//        abstract configure : q:byte[] -> byte[]

        //[<OperationContract(Name = "monitor")>]
        //abstract monitor : q:byte[] -> byte[]

        [<OperationContract(Name = "ping")>]
        abstract ping : q:byte[] -> byte[]


    type WorkerNodeResponseHandlerData =
        {
            url : string
            communicationType : WorkerNodeServiceAccessInfo
        }


    /// Low level WCF messaging client.
    type WorkerNodeResponseHandler (i : WorkerNodeServiceAccessInfo) =
        let n = i.value.netTcpServiceInfo
        let c = NetTcpCommunication NoSecurity
        let url = i.value.getUrl c
        let tryGetWcfService() = tryGetWcfService<IWorkerNodeWcfService> c url

        //let configureWcfErr e = e |> ConfigureWcfErr |> WorkerNodeWcfErr |> WorkerNodeServiceErr
        //let monitorWcfErr e = e |> MonitorWcfErr |> WorkerNodeWcfErr |> WorkerNodeServiceErr
        let pingWcfErr e = e |> PingWcfErr |> WorkerNodeWcfErr

        //let monitorImpl p = tryCommunicate tryGetWcfService (fun service -> service.monitor) monitorWcfErr p
        let pingImpl() = tryCommunicate tryGetWcfService (fun service -> service.ping) pingWcfErr ()

        interface IWorkerNodeService with
            //member _.monitor p = monitorImpl p
            member _.ping() = pingImpl()

        //new (i : WorkerNodeServiceAccessInfo, communicationType, securityMode) =
        //    WorkerNodeResponseHandler(i.value.getUrl communicationType, communicationType, securityMode)


//    let private workerNodeRunner : Lazy<ClmResult<WorkerNodeRunner>> =
//        new Lazy<ClmResult<WorkerNodeRunner>>(fun () -> WorkerNodeRunner.create serviceAccessInfo)


//    let tryStartWorkerNodeRunner() =
//        match workerNodeRunner.Value with
//        | Ok service -> service.start() |> Ok
//        | Error e -> Error e


//    type WorkerNodeWcfService<'D, 'P>(messagingDataVersion, data, tryRunSolverProcess) =
////        let toConfigureError f = f |> ConfigureWcfErr |> WorkerNodeWcfErr |> WorkerNodeServiceErr
//        //let toMonitorError f = f |> MonitorWcfErr |> WorkerNodeWcfErr |> WorkerNodeServiceErr
//        let toPingError f = f |> PingWcfErr |> WorkerNodeWcfErr

//        let tryCreateWorkerNodeRunner() =
//            match  WorkerNodeRunner<'D, 'P>.create messagingDataVersion data tryRunSolverProcess with
//            | Ok service ->
//                service.start() |> ignore
//                Ok service
//            | Error e -> Error e

//        let workerNodeRunner = new Lazy<Result<WorkerNodeRunner<'D, 'P>, DistributedProcessingError>>(fun () -> tryCreateWorkerNodeRunner())

////        let configure c = workerNodeRunner.Value |> Rop.bind (fun e -> e.configure c)
//        //let monitor (_ : WorkerNodeMonitorParam) = workerNodeRunner.Value |> Rop.bind (fun e -> e.getState() |> Ok)
//        let ping () = workerNodeRunner.Value |> Rop.bind (fun _ -> Ok())

//        interface IWorkerNodeWcfService with
////            member _.configure b = tryReply configure toConfigureError b
//            //member _.monitor b = tryReply monitor toMonitorError b
//            member _.ping b = tryReply ping toPingError b


//    type WorkerNodeWcfServiceImpl<'D, 'P> = WcfService<WorkerNodeWcfService<'D, 'P>, IWorkerNodeWcfService, WorkerNodeServiceInfo>


//    type WorkerNodeWorker<'D, 'P>(logger: ILogger<WorkerNodeWorker<'D, 'P>>, v : MessagingDataVersion, tryRunSolverProcess) =
//        inherit BackgroundService()

//        let tyGetHost() : WcfResult<WcfService> =
//            let serviceData = failwith ""

//            match serviceData with
//            | Ok data ->
//                printfn $"tryGetHost: Got MessagingServiceData: '{data}'."
//                let service = new WorkerNodeWcfService<'D, 'P>(v, data, tryRunSolverProcess)
//                Ok service
//            | Error e ->
//                printfn $"tryGetHost: Error: %A{e}"
//                Error e

//            //let wcfLogger =  Logger.defaultValue
//            //let clmLogger =  Logger.defaultValue

//            //match serviceAccessInfo with
//            //| Ok data ->
//            //    match tryGetServiceData data.workerNodeServiceAccessInfo.value wcfLogger data with
//            //    | Ok serviceData ->
//            //        let service = WorkerNodeWcfServiceImpl.tryGetService serviceData
//            //        let r = tryStartWorkerNodeRunner()
                
//            //        match r with
//            //        | Ok _ -> ignore()
//            //        | Error e -> clmLogger.logCritData e

//            //        service
//            //    | Error e ->
//            //        wcfLogger.logCritData e
//            //        Error e
//            //| Error e ->
//            //    clmLogger.logCritData e

//            //    // TODO kk:20201213 - Here we are forced to "downgrade" the error type because there is no conversion.
//            //    e.ToString() |> WcfCriticalErr |> Error

//        let hostRes = Lazy<WcfResult<WcfService>>(fun () -> tyGetHost())
//        let getHost() = hostRes.Value

//        override _.ExecuteAsync(_: CancellationToken) =
//            async {
//                printfn "WorkerNodeWorker::Executing..."
//                logger.LogInformation("Executing...")

//                match getHost() with
//                | Ok host -> do! host.runAsync()
//                | Error e -> logger.LogCritical $"Error: %A{e}"
//            }
//            |> Async.StartAsTask
//            :> Task

//        override _.StopAsync(_: CancellationToken) =
//            async {
//                logger.LogInformation("Stopping...")

//                match getHost() with
//                | Ok host -> do! host.stopAsync()
//                | Error e -> logger.LogCritical$"Error: %A{e}"
//            }
//            |> Async.StartAsTask
//            :> Task

