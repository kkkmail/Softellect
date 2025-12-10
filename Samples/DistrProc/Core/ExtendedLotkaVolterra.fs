namespace Softellect.Samples.DistrProc.Core

open System
open System.Threading
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.Sys.Logging
open Softellect.Samples.DistrProc.Core.Primitives
open Softellect.Math.Evolution

module ExtendedLotkaVolterra =
    // Hunting consists of the following events:
    // 1. Predator decides to hunt. This is based on the predator state and the prey availability in the vicinity.
    // 2. Predator searches for prey. This is a quasi-random event, and it depends on the probability of the encounter.
    // 3. If prey is found, then the predator tries to catch it. This is based on the dexterity of both predator and prey.
    //    This process is not instantaneous, and it takes some time to catch the prey (or not).
    // 4. All processes consume some energy, so the predator and prey states are updated accordingly.
    // 5. The events happen during an evolution epoch, which is a discrete time unit in the evolution of a population.
    //    Multiple events can happen during a single epoch.

    // 5. If prey is caught, then the predator consumes it, and its nutrition state is updated.
    // 6. If prey is not caught, then it escapes, and its nutrition state is updated.
    // 7. If prey is not found, then the predator's nutrition state is updated.


    // Hunting consists of three main events:
    // 1. Prey is not found.
    // 2. Prey is found, but escapes.
    // 3. Prey is found and captured.
    // An evolution time is a discrete time unit in the evolution of a population.

    type Age =
        | Age of EvolutionEpoch

        member this.value = let (Age v) = this in v
        member this.increment() = this.value.increment() |> Age


    /// A state of nutrition for an animal.
    /// Takes a value between -1 and 1, where 0 means optimal nutrition for SURVIVABILITY,
    /// -1 means a starving-to-death animal,
    /// and 1 means a completely overfed animal, which will die from obesity.
    ///
    /// We shall state that 0 does not mean a perfect nutrition state (e.g., well-fed animal),
    /// but rather an optimal one for survivability.
    ///
    /// Both (-1) and (1) are fatal states and so are realistically unattainable.
    ///
    /// Nutrition state affects the ability of an animal to reproduce and survive.
    ///
    /// One may argue that the optimal nutrition states for survivability and reproduction could be different.
    /// For example, cactuses are known to blossom only after a drought. We probably won't go this route for now.
    /// However, it is straightforward to implement by providing different functions for dexterity and reproduction rate.
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
    /// which is translated here into a function of <see cref="AnimalState"/>.
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


    // Weight -> WeightFunction
    // Edibility (come up with a better name - that's how much will be used by a predator) -> EdibilityFunction
    // "Recycling" (and related) - that's how much can be used to replenish the soil.


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


    /// Encapsulates a need for feeding.
    type FeedingNeed =
        | NeedsFeeding
        | MayNeedFeeding of double // A value between 0 and 1, where 0 means no need for feeding, and 1 means a strong need for feeding.


    /// Hunting results affects the state of the prey and predator animals.
    /// Change to NutritionState is different based on the result.
    type HuntingResult =
        | PreyNotFound
        | PreyEscaped
        | PreyCaptured


    /// Current food consumption rate of an animal.
    ///
    /// Combination of threat and food availability.
    type FoodConsumptionRate =
        | FoodConsumptionRate of double

        member this.value = let (FoodConsumptionRate v) = this in v


    type AnimalFunctions =
        {
            dexterityFunction: DexterityFunction
            reproductionRateFunction: ReproductionRateFunction
            mortalityRateFunction: MortalityRateFunction
            // foodConsumptionRate: FoodConsumptionRate
        }


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
        | Waste
