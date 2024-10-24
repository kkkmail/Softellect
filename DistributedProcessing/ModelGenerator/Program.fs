namespace Softellect.DistributedProcessing.ModelGenerator

open Softellect.DistributedProcessing.Proxy.ModelGenerator
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Primitives

module Program =

    /// Parameter solverId is passed first, so that it can be baked in and then reused with different proxies (= models).
    let generateModel<'I, 'D> solverId (userProxy : UserProxy<'I, 'D>) =
        let ctx =
            {
                userProxy = userProxy
                systemProxy = SystemProxy.create()
                solverId = solverId
            }

        // Get the initial data out of command line parameters and other data.
        let input = ctx.userProxy.getInitialData()

        let modelData =
            {
                solverInputParams = ctx.userProxy.getSolverInputParams input
                solverOutputParams = ctx.userProxy.getSolverOutputParams input
                solverId = ctx.solverId
                modelData = ctx.userProxy.generateModelContext input
            }

        let binaryData = modelData.toModelBinaryData()
        let runQueueId = RunQueueId.getNewId()

        match ctx.systemProxy.saveModelData runQueueId ctx.solverId binaryData with
        | Ok() -> Ok()
        | Error e ->
            printfn $"generateModel<{typeof<'I>.Name}, {typeof<'D>.Name}> - solverId: '{solverId}', ERROR: '%A{e}'."
            Error e
