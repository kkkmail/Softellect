namespace Softellect.Samples.DistrProc.Core

open System
open System.Threading
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Logging
open Softellect.Samples.DistrProc.Core.Primitives

module ExtendedLotkaVolterra =

    /// An evolution epoch is a discrete time unit in the evolution of a population.
    /// One day is likely too fine-grained for evolution, and one year is likely too coarse.
    /// So, something like a week or a month is probably a good choice.
    type EvolutionEpoch =
        | EvolutionEpoch of int

        member this.value = let (EvolutionEpoch v) = this in v
        member this.increment() = this.value + 1 |> EvolutionEpoch


    type Age =
        | Age of EvolutionEpoch

        member this.value = let (Age v) = this in v
        member this.increment() = this.value.increment() |> Age


    /// A state of nutrition for an animal.
    /// Takes a value between -1 and 1, where 0 means optimal nutrition,
    /// -1 means a starving-to-death animal,
    /// and 1 means a completely overfed animal, which will die from obesity.
    ///
    /// We shall state that 0 does not mean a perfect nutrition state (e.g., well-fed animal),
    /// but rather an optimal one for survivability and reproduction.
    ///
    /// Both (-1) and (1) are fatal states and so are realistically unattainable.
    ///
    /// Nutrition state affects the ability of an animal to reproduce and survive.
    ///
    /// One may argue that the optimal nutrition states for survivability and reproduction could be different.
    /// For example, cactuses are known to blossom only after a drought. We probably won't go this route for now.
    type NutritionState =
        | NutritionState of double

        member this.value = let (NutritionState v) = this in v


    /// An animal state is described by its age and nutrition state.
    type AnimalState =
        {
            age: Age
            nutritionState: NutritionState
        }


    /// Dexterity is a combination of speed, agility, and coordination,
    /// which is translated here into a function of age and nutrition state.
    ///
    /// A prey animal with high dexterity is more likely to escape predators,
    /// and a predator with high dexterity is more likely to catch prey.
    ///
    /// Eventually, it is dexterity of prey ws dexterity of predator that determines the outcome of their interaction.
    type Dexterity =
        | Dexterity of double

        member this.value = let (Dexterity v) = this in v


    /// An encapsulation of dexterity function for an animal.
    type DexterityFunction =
        | DexterityFunction of (AnimalState -> Dexterity)

        member this.invoke = let (DexterityFunction f) = this in f


    /// Current reproduction rate of an animal.
    type ReproductionRate =
        | ReproductionRate of double

        member this.value = let (ReproductionRate v) = this in v


    /// An encapsulation of reproduction rate function for a given animal.
    type ReproductionRateFunction    =
        | ReproductionRateFunction of (AnimalState -> ReproductionRate)

        member this.invoke = let (ReproductionRateFunction f) = this in f


    /// Current mortality rate of an animal.
    type MortalityRate =
        | MortalityRate of double

        member this.value = let (MortalityRate v) = this in v


    /// An encapsulation of mortality rate function for a given animal.
    type MortalityRateFunction    =
        | MortalityRateFunction of (AnimalState -> MortalityRate)

        member this.invoke = let (MortalityRateFunction f) = this in f


    type Animal =
        {
            age: Age
            nutritionState: NutritionState
            mortalityRate: MortalityRate
        }


    type Rabbit =
        {
            alpha: double
            beta: double
            gamma: double
            delta: double
        }


    type X =
        | Soil
        | Grass // Add Seed ???
        | Rabbit
        | Fox
