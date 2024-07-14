namespace Softellect.MessagingService

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open Softellect.Wcf.Common
open Softellect.Wcf.Service
open Softellect.MessagingService.CommandLine
open Softellect.Sys.Logging
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service


type private MessagingWcfServiceImpl<'D> = WcfService<MessagingWcfService<'D>, IMessagingWcfService, MessagingServiceData<'D>>


type MsgWorker<'D>(logger: ILogger<MsgWorker<'D>>) =
    inherit BackgroundService()

    static let tyGetHost() =
        printfn $"tyGetHost: Getting MessagingServiceData..."
        let messagingServiceData = getMessagingServiceData Logger.defaultValue

        match messagingServiceData with
        | Ok data ->
            printfn $"tyGetHost: Got MessagingServiceData: '{data}'."
            let service = MessagingWcfServiceImpl<'D>.tryGetService data
            MessagingService<'D>.tryStart() |> ignore
            service
        | Error e ->
            printfn $"tyGetHost: Error: %A{e}"
            Error e

    static let hostRes = Lazy<WcfResult<WcfService>>(fun () -> tyGetHost())

    override _.ExecuteAsync(_: CancellationToken) =
        async {
            logger.LogInformation("Executing...")

            match hostRes.Value with
            | Ok host -> do! host.runAsync()
            | Error e -> logger.LogCritical$"Error: %A{e}"
        }
        |> Async.StartAsTask
        :> Task

    override _.StopAsync(_: CancellationToken) =
        async {
            logger.LogInformation("Stopping...")

            match hostRes.Value with
            | Ok host -> do! host.stopAsync()
            | Error e -> logger.LogCritical$"Error: %A{e}"
        }
        |> Async.StartAsTask
        :> Task
