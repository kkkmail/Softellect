namespace Softellect.MessagingService

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open Softellect.Wcf.Common
open Softellect.Wcf.Service
open Softellect.Sys.Logging
open Softellect.MessagingService.CommandLine
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Messaging.Primitives

module Worker =

    type MessagingWcfServiceImpl<'D> = WcfService<MessagingWcfService<'D>, IMessagingWcfService, MessagingServiceData<'D>>


    type MsgWorker<'D>(logger: ILogger<MsgWorker<'D>>, v : MessagingDataVersion, m : MessagingService<'D>, w : MessagingWcfServiceImpl<'D>) =
        inherit BackgroundService()

        let tryGetHost() =
            printfn $"tryGetHost: Getting MessagingServiceData..."
            let messagingServiceData = getMessagingServiceData<'D> Logger.defaultValue v

            match messagingServiceData with
            | Ok data ->
                printfn $"tryGetHost: Got MessagingServiceData: '{data}'."
                let service = w.tryGetService ()
                (m :> IMessagingService<'D>).tryStart() |> ignore
                service
            | Error e ->
                printfn $"tryGetHost: Error: %A{e}"
                Error e

        let hostRes = Lazy<WcfResult<WcfService>>(fun () -> tryGetHost())
        let getHost() = hostRes.Value

        override _.ExecuteAsync(_: CancellationToken) =
            async {
                logger.LogInformation("Executing...")

                match getHost() with
                | Ok host -> do! host.runAsync()
                | Error e -> logger.LogCritical$"Error: %A{e}"
            }
            |> Async.StartAsTask
            :> Task

        override _.StopAsync(_: CancellationToken) =
            async {
                logger.LogInformation("Stopping...")

                match getHost() with
                | Ok host -> do! host.stopAsync()
                | Error e -> logger.LogCritical$"Error: %A{e}"
            }
            |> Async.StartAsTask
            :> Task
