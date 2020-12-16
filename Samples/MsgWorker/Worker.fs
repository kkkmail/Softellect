namespace Softellect.Samples.Msg.WcfWorker

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open Softellect.Samples.Msg.ServiceInfo.EchoMsgServiceInfo

type Worker(logger: ILogger<Worker>) =
    inherit BackgroundService()

    static let hostRes =
        match echoMsgServiceDataRes with
        | Ok data -> EchoMessagingWcfServiceImpl.tryGetService data
        | Error e -> Error e

    override _.ExecuteAsync(_: CancellationToken) =
        async {
            logger.LogInformation("Executing...")

            match hostRes with
            | Ok host -> do! host.runAsync()
            | Error e -> logger.LogCritical(sprintf "Error: %A" e)
        }
        |> Async.StartAsTask
        :> Task

    override _.StopAsync(_: CancellationToken) =
        async {
            logger.LogInformation("Stopping...")

            match hostRes with
            | Ok host -> do! host.stopAsync()
            | Error e -> logger.LogCritical(sprintf "Error: %A" e)
        }
        |> Async.StartAsTask
        :> Task
