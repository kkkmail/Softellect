namespace Softellect.DistributedProcessing.ModelGenerator

open Softellect.DistributedProcessing.ModelGenerator.Primitives
open Softellect.DistributedProcessing.Primitives.Common

module Program =

    let generatorModel<'I, 'D, 'P, 'X, 'C> (userProxy : UserProxy<'I, 'D>) solverId input =
        let modelData =
            {
                solverInputParams = userProxy.getSolverInputParams input
                solverOutputParams = userProxy.getSolverOutputParams input
                solverId = solverId
                modelData = userProxy.generateModel input
            }

        let binaryData = modelData.toModelBinaryData()


        let ctx = { userProxy = userProxy; systemProxy = SystemProxy.create(); solverId = solverId }
        failwith "modelGeneratorMain is not implemented yet"
