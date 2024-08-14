namespace Softellect.Sys

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open Argu

module Worker =

    [<CliPrefix(CliPrefix.None)>]
    type WorkerArguments<'A when 'A :> IArgParserTemplate> =
        | [<Unique>] [<First>] [<AltCommandLine("r")>] Run of ParseResults<'A>
        | [<Unique>] [<First>] [<AltCommandLine("s")>] Save of ParseResults<'A>

        static member fromArgu c a : list<WorkerArguments<'A>> = a |> List.map (fun e -> c e)


    type WorkerTask<'R, 'A when 'A :> IArgParserTemplate> =
        | RunServiceTask of (unit -> unit)
        | SaveSettingsTask of (unit -> unit)

        member task.run () =
            match task with
            | RunServiceTask r -> r()
            | SaveSettingsTask s -> s()

        static member private tryCreateRunServiceTask r (p : list<WorkerArguments<'A>>) : WorkerTask<'R, 'A> option =
            p |> List.tryPick (fun e -> match e with | Run p -> r p |> RunServiceTask |> Some | _ -> None)

        static member private tryCreateSaveSettingsTask s (p : list<WorkerArguments<'A>>) : WorkerTask<'R, 'A> option =
            p |> List.tryPick (fun e -> match e with | Save p -> s p |> SaveSettingsTask |> Some | _ -> None)

        static member tryCreate r s p : WorkerTask<'R, 'A> option =
            [
                WorkerTask.tryCreateRunServiceTask r
                WorkerTask.tryCreateSaveSettingsTask s
            ]
            |> List.tryPick (fun e -> e p)


    //type Worker<'D, 'W, 'E when 'W : (member runAsync: unit -> Async<unit>)>(logger: ILogger<Worker<'D, 'W, 'E>>, v : MessagingDataVersion) =
    //    inherit BackgroundService()

    //    let tryGetHost() : Result<'W, 'E> =
    //        printfn $"tryGetHost: Getting MessagingServiceData..."
    //        let messagingServiceData = getMessagingServiceData<'D> Logger.defaultValue v

    //        match messagingServiceData with
    //        | Ok data ->
    //            printfn $"tryGetHost: Got MessagingServiceData: '{data}'."
    //            let service = MessagingWcfServiceImpl<'D>.tryGetService data
    //            MessagingService<'D>.tryStart() |> ignore
    //            service
    //        | Error e ->
    //            printfn $"tryGetHost: Error: %A{e}"
    //            Error e

    //    let hostRes = Lazy<Result<'W, 'E>>(fun () -> tryGetHost())
    //    let getHost() = hostRes.Value

    //    override _.ExecuteAsync(_: CancellationToken) =
    //        async {
    //            logger.LogInformation("Executing...")

    //            match getHost() with
    //            | Ok host -> do! host.runAsync()
    //            | Error e -> logger.LogCritical$"Error: %A{e}"
    //        }
    //        |> Async.StartAsTask
    //        :> Task

    //    override _.StopAsync(_: CancellationToken) =
    //        async {
    //            logger.LogInformation("Stopping...")

    //            match getHost() with
    //            | Ok host -> do! host.stopAsync()
    //            | Error e -> logger.LogCritical$"Error: %A{e}"
    //        }
    //        |> Async.StartAsTask
    //        :> Task

