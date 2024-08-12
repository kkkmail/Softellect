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
open Softellect.Messaging.Primitives

module Worker =

    type private MessagingWcfServiceImpl<'D> = WcfService<MessagingWcfService<'D>, IMessagingWcfService, MessagingServiceData<'D>>


    type MsgWorker<'D>(logger: ILogger<MsgWorker<'D>>, v : MessagingDataVersion) =
        inherit BackgroundService()

        static let mutable messagingDataVersion = 0

        static let tryGetHost() =
            printfn $"tryGetHost: Getting MessagingServiceData..."
            let messagingServiceData = getMessagingServiceData<'D> Logger.defaultValue (MessagingDataVersion messagingDataVersion)

            match messagingServiceData with
            | Ok data ->
                printfn $"tryGetHost: Got MessagingServiceData: '{data}'."
                let service = MessagingWcfServiceImpl<'D>.tryGetService data
                MessagingService<'D>.tryStart() |> ignore
                service
            | Error e ->
                printfn $"tryGetHost: Error: %A{e}"
                Error e

        static let hostRes = Lazy<WcfResult<WcfService>>(fun () -> tryGetHost())
        static let getHost() = hostRes.Value

        override _.ExecuteAsync(_: CancellationToken) =
            async {
                logger.LogInformation("Executing...")
                Interlocked.CompareExchange(&messagingDataVersion, v.value, 0) |> ignore

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
