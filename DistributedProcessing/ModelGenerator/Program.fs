namespace Softellect.DistributedProcessing.ModelGenerator

open Softellect.DistributedProcessing.Proxy.ModelGenerator
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Primitives

module Program =

    let generateModel<'I, 'D> (userProxy : UserProxy<'I, 'D>) solverId argv =
        let ctx =
            {
                userProxy = userProxy
                systemProxy = SystemProxy.create()
                solverId = solverId
            }

        // Get the initial data out of command line parameters.
        let input = ctx.userProxy.getInitialData argv

        let modelData =
            {
                solverInputParams = ctx.userProxy.getSolverInputParams input
                solverOutputParams = ctx.userProxy.getSolverOutputParams input
                solverId = ctx.solverId
                modelData = ctx.userProxy.generateModel input
            }

        let binaryData = modelData.toModelBinaryData()
        let runQueueId = RunQueueId.getNewId()

        match ctx.systemProxy.saveModelData runQueueId ctx.solverId binaryData with
        | Ok() -> Ok()
        | Error e ->
            printfn $"generateModel<{typeof<'I>.Name}, {typeof<'D>.Name}> - solverId: '{solverId}', ERROR: '%A{e}'."
            Error e
