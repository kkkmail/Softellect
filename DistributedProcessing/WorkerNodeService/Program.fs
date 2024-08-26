namespace Softellect.DistributedProcessing.WorkerNodeService

open Argu
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Softellect.Messaging.Primitives
open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.WorkerNodeService.Worker
open Softellect.DistributedProcessing.WorkerNodeService.CommandLine
open Softellect.Sys.ExitErrorCodes

module Program =

    type WorkerNodeProgramData<'D, 'P> =
        {
            x : int
            y : DistributedProcessingMessageData<'D, 'P>
        }

    //let private createHostBuilder<'D, 'P> (v : MessagingDataVersion) =
    //    Host.CreateDefaultBuilder()
    //        .UseWindowsService()
    //        .ConfigureServices(fun hostContext services ->
    //            services.AddSingleton(v) |> ignore
    //            services.AddHostedService<MsgWorker<'D>>() |> ignore)


    let main<'D, 'P> workerNodeProgramName data argv =
        //let runHost() = createHostBuilder<'D>(v).Build().Run()

        //try
        //    let parser = ArgumentParser.Create<MsgSvcArguArgs>(programName = messagingProgramName)
        //    let results = (parser.Parse argv).GetAllResults() |> MsgSvcArgs.fromArgu convertArgs

        //    let run p =
        //        getParams v p |> ignore
        //        runHost

        //    match MessagingServiceTask.tryCreate run (getSaveSettings v) results with
        //    | Some task -> task.run()
        //    | None ->  runHost()

        //    CompletedSuccessfully

        //with
        //| exn ->
        //    printfn $"%s{exn.Message}"
        //    UnknownException
        0
