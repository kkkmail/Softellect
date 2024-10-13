namespace Softellect.DistributedProcessing.ModelGenerator

open Softellect.DistributedProcessing.Primitives.Common

module Primitives =

    type UserProxy<'I, 'D> =
        {
            generateModel : 'I -> 'D
            getSolverInputParams : 'I -> SolverInputParams
            getSolverOutputParams : 'I -> SolverOutputParams
        }

    type SystemProxy =
        {
            x : int
        }

        static member create() : SystemProxy = failwith ""


    type  ModelGeneratorContext<'I, 'D> =
        {
            userProxy : UserProxy<'I, 'D>
            systemProxy : SystemProxy
            solverId : SolverId
        }
