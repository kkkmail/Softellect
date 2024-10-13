﻿namespace Softellect.DistributedProcessing.ModelGenerator

open Softellect.DistributedProcessing.Proxy.ModelGenerator
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Primitives

module Program =

    let generateModel<'I, 'D> (userProxy : UserProxy<'I, 'D>) solverId input =
        let ctx =
            {
                userProxy = userProxy
                systemProxy = SystemProxy.create()
                solverId = solverId
            }

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
            printfn $"generateModel<{typeof<'I>.Name}, {typeof<'D>.Name}> - ERROR: '{e}'."
            Error e