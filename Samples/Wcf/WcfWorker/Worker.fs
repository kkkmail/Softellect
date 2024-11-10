namespace Softellect.Samples.Wcf.WcfWorker

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open Softellect.Samples.Wcf.ServiceInfo.EchoWcfServiceInfo
open Softellect.Samples.Wcf.Service.EchoWcfService

type Worker(logger: ILogger<Worker>) =
    inherit BackgroundService()

    //static let hostRes =
    //    match echoWcfServiceDataRes with
    //    | Ok data -> EchoWcfServiceImpl.tryGetService data
    //    | Error e -> Error e

    override _.ExecuteAsync(_: CancellationToken) =
        //async {
        //    logger.LogInformation("Executing...")

        //    match hostRes with
        //    | Ok host -> do! host.runAsync()
        //    | Error e -> logger.LogCritical$"Error: %A{e}"
        //}
        //|> Async.StartAsTask
        //:> Task
        failwith "Not implemented yet."

    override _.StopAsync(_: CancellationToken) =
        //async {
        //    logger.LogInformation("Stopping...")

        //    match hostRes with
        //    | Ok host -> do! host.stopAsync()
        //    | Error e -> logger.LogCritical$"Error: %A{e}"
        //}
        //|> Async.StartAsTask
        //:> Task
        failwith "Not implemented yet."
