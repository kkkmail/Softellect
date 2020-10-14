namespace fsharpworker

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

type Worker(logger: ILogger<Worker>) =
    inherit BackgroundService()

    override _.StartAsync(ct: CancellationToken) =
        async {
                do! Async.Sleep(0)
        }
        |> Async.StartAsTask
        :> Task


    override _.StopAsync(ct: CancellationToken) =
        async {
                do! Async.Sleep(0)
        }
        |> Async.StartAsTask
        :> Task


    override _.ExecuteAsync(ct: CancellationToken) =
        async {
            while not ct.IsCancellationRequested do
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now)
                do! Async.Sleep(1000)
        }
        |> Async.StartAsTask
        :> Task
