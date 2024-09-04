namespace Softellect.DistributedProcessing.SolverRunner

//open Argu
//open Microsoft.Extensions.DependencyInjection
//open Microsoft.Extensions.Hosting
//open Softellect.Messaging.Primitives
//open Softellect.DistributedProcessing.Primitives
//open Softellect.DistributedProcessing.WorkerNodeService.Worker
//open Softellect.DistributedProcessing.WorkerNodeService.CommandLine
//open Softellect.DistributedProcessing.WorkerNodeService.WorkerNode
//open Softellect.Sys.ExitErrorCodes
//open Softellect.Wcf.Program
//open Softellect.DistributedProcessing.WorkerNodeService.Proxy

module Program =
    let x = 1

    ////type WorkerNodeProgramData<'D, 'P> =
    ////    {
    ////        x : int
    ////        y : DistributedProcessingMessageData<'D, 'P>
    ////    }

    ////let private createHostBuilder<'D, 'P> (v : MessagingDataVersion) =
    ////    Host.CreateDefaultBuilder()
    ////        .UseWindowsService()
    ////        .ConfigureServices(fun hostContext services ->
    ////            services.AddSingleton(v) |> ignore
    ////            services.AddHostedService<MsgWorker<'D>>() |> ignore)


    ////let main<'D, 'P> workerNodeProgramName data argv =
    ////    //let runHost() = createHostBuilder<'D>(v).Build().Run()

    ////    //try
    ////    //    let parser = ArgumentParser.Create<MsgSvcArguArgs>(programName = messagingProgramName)
    ////    //    let results = (parser.Parse argv).GetAllResults() |> MsgSvcArgs.fromArgu convertArgs

    ////    //    let run p =
    ////    //        getParams v p |> ignore
    ////    //        runHost

    ////    //    match MessagingServiceTask.tryCreate run (getSaveSettings v) results with
    ////    //    | Some task -> task.run()
    ////    //    | None ->  runHost()

    ////    //    CompletedSuccessfully

    ////    //with
    ////    //| exn ->
    ////    //    printfn $"%s{exn.Message}"
    ////    //    UnknownException
    ////    0

    //let main<'D, 'P> workerNodeProgramName (data : WorkerNodeRunnerData<'D, 'P>) argv =
    //    //printfn $"main<{typeof<'D>.Name}> - data.messagingServiceAccessInfo = '{data.messagingServiceAccessInfo}'."

    //    let saveSettings() =
    //        //let result = updateMessagingServiceAccessInfo data.messagingServiceAccessInfo
    //        //printfn $"saveSettings - result: '%A{result}'."
    //        failwith ""

    //    let configureServices (services : IServiceCollection) =
    //        let runner = new WorkerNodeRunner<'D, 'P>(data)
    //        services.AddSingleton<IHostedService>(runner :> IHostedService) |> ignore

    //    let programData =
    //        {
    //            serviceAccessInfo = data.workerNodeServiceInfo.workerNodeServiceAccessInfo
    //            getService = fun () -> new WorkerNodeService(data.workerNodeServiceInfo) :> IWorkerNodeService
    //            getWcfService = fun service -> new WorkerNodeWcfService(service)
    //            saveSettings = saveSettings
    //            configureServices = Some configureServices
    //        }

    //    main<IWorkerNodeService, IWorkerNodeWcfService, WorkerNodeWcfService> workerNodeProgramName programData argv
