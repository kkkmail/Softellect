namespace Softellect.DistributedProcessing.ModelGenerator

open Softellect.DistributedProcessing.Proxy.ModelGenerator
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.AppSettings

module Program =

    /// Parameters systemProxy and solverId are passed first, so that they can be baked in and then reused
    /// with different proxies (= models).
    let generateModel<'I, 'D> (systemProxy : ModelGeneratorSystemProxy) solverId (userProxy : ModelGeneratorUserProxy<'I, 'D>) =
        setLogLevel()

        let ctx =
            {
                userProxy = userProxy
                systemProxy = systemProxy
                solverId = solverId
            }

        // Get the initial data out of command line parameters and other data.
        let input = ctx.userProxy.getInitialData()

        let modelData =
            {
                solverInputParams = ctx.userProxy.getSolverInputParams input
                solverOutputParams = ctx.userProxy.getSolverOutputParams input
                solverId = ctx.solverId
                modelData = ctx.userProxy.generateModelData input
            }

        Logger.logInfo $"generateModel<{typeof<'I>.Name}, {typeof<'D>.Name}> - solverId: '{solverId}', modelData.GetType().Name = '%A{modelData.GetType().Name}', modelData: '%A{modelData}'."

        let binaryData = modelData.toModelBinaryData()
        let runQueueId = RunQueueId.getNewId()

        match ctx.systemProxy.saveModelData runQueueId ctx.solverId binaryData with
        | Ok() -> Ok binaryData
        | Error e ->
            Logger.logError $"generateModel<{typeof<'I>.Name}, {typeof<'D>.Name}> - solverId: '{solverId}', ERROR: '%A{e}'."
            Error e
