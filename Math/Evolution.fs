namespace Softellect.Math

open System
open System.Collections.Generic
open FSharp.Collections
open MathNet.Numerics.Distributions
open Softellect.Math.Primitives
open Softellect.Math.Sparse

module Evolution =

    /// An evolution epoch is a discrete time unit in the Poisson evolution.
    type EvolutionEpoch =
        | EvolutionEpoch of int

        member this.value = let (EvolutionEpoch v) = this in v
        member this.increment() = this.value + 1 |> EvolutionEpoch

    /// A constant value for the epoch, used in granular evolution time calculations.
        static member epochValue = 1.0


    /// A more granular time flow within an evolution epoch.
    /// Multiple events can happen during a single epoch.
    type EvolutionTime =
        | EpochExpired
        | EvolutionTime of double


    /// A combination of an event and current evolution time within an epoch.
    type EventState<'E> =
        {
            event : 'E
            evolutionTime : EvolutionTime
        }


    /// Represents a wait time in a Poisson process.
    type WaitTime =
        | InfiniteWaitTime
        | FiniteWaitTime of double


    /// Represents the result of an event in a Poisson process.
    type EventResult<'R> =
        | EventResult of 'R

        member r.value = let (EventResult v) = r in v


    /// Encapsulates a proxy for handling events in a multistep Poisson process
    /// when the next step depends on the outcome of the previous one.
    type EventProxy<'E, 'R> =
        {
            /// Evaluates the current event and returns a result.
            evaluateEvent : 'E -> 'R

            /// Gets the next event based on the result of the previous event.
            nextEvent : 'R -> 'E

            /// Gets the next event based on the epoch expiration.
            endOfEpochEvent : 'E -> 'E

            /// Gets the wait time for the current event.
            waitTime : 'E -> WaitTime
        }

        /// Attempts to wait for the current event to happen.
        ///
        /// If the event happens before the end of the epoch, then evaluates the event and returns the next event
        /// and updates the evolution time.
        ///
        /// If the event does not happen before the end of the epoch, then returns the end of epoch event and updates
        /// the evolution time to the end of epoch.
        member p.evolve (e : EventState<'E>) : EventState<'E> =
            match e.evolutionTime with
            | EpochExpired -> e // No evolution time left, return the event as is.
            | EvolutionTime v ->
                // Event did not happen till the end of the epoch.
                let ee() =
                    {
                        event = p.endOfEpochEvent e.event
                        evolutionTime = EpochExpired
                    }

                match p.waitTime e.event with
                | InfiniteWaitTime -> ee()
                | FiniteWaitTime w ->
                    let nv = v + w

                    match nv > EvolutionEpoch.epochValue with
                    | true -> ee()
                    | false ->
                        {
                            event = p.evaluateEvent e.event |> p.nextEvent
                            evolutionTime = EvolutionTime nv
                        }


    /// Returns the next random number of events for a given lambda.
    let inline private poissonSample converter rnd lambda =
        match lambda with
        | lambda when lambda <= 0.0 -> 0.0
        | lambda when lambda <= 2e9 ->
            // Use MathNet.Numerics.Distributions for small lambda
            try
                double (Poisson.Sample(rnd, lambda))
            with
            | e -> failwith $"lambda: {lambda}, exception: {e}"
        | _ ->
            // Use Gaussian approximation for large lambda
            let mu = lambda
            let sigma = sqrt lambda
            let sample = Normal.Sample(rnd, mu, sigma)
            Math.Round(sample)
        |> converter


    /// Returns the next random wait time until the first event for a Poisson process with rate lambda.
    /// The wait time follows an exponential distribution with parameter lambda.
    let inline private exponentialWaitTime rnd lambda =
        match lambda with
        | lambda when lambda <= 0.0 -> InfiniteWaitTime
        | _ ->
            try
                Exponential.Sample(rnd, lambda) |> FiniteWaitTime
            with
            | e -> failwith $"lambda: {lambda}, exception: {e}"


    /// State for the deterministic Poisson sampler
    type DeterministicPoissonState =
        {
            mutable Remainder: float  // Stores the fractional part between calls
        }

        static member create() = { Remainder = 0.0 }


    /// Helper function that implements deterministic Poisson sampling with state
    let deterministicPoissonSample (state: DeterministicPoissonState) (lambda: float) : int64 =
        // Add the remainder from previous call
        let adjustedLambda = lambda + state.Remainder

        // Split lambda into integer and fractional parts
        let intPart = floor adjustedLambda
        let fracPart = adjustedLambda - intPart

        // Store the fractional part for the next call
        state.Remainder <- fracPart

        // Return the integer part as the sample
        int64 intPart


    /// Encapsulation of a Poisson distribution sampler.
    /// It takes a value of lambda and returns the next random number of events.
    type PoissonSampler<'T when 'T: equality and 'T: comparison> =
        {
            nextNumberOfEvents : float -> 'T
            nextWaitTime : float -> WaitTime
        }

        member inline r.next lambda = r.nextNumberOfEvents lambda
        static member inline create converter rnd : PoissonSampler<'T> =
            {
                nextNumberOfEvents = poissonSample converter rnd
                nextWaitTime = exponentialWaitTime rnd
            }

        static member inline deterministic() =
            let state = DeterministicPoissonState.create()

            {
                nextNumberOfEvents = (deterministicPoissonSample state)
                nextWaitTime = fun lambda ->
                    if lambda <= 0.0 then InfiniteWaitTime
                    else 1.0 / lambda |> FiniteWaitTime
            }


    type EvolutionType =
        | DifferentialEvolution
        | DiscreteEvolution


    type EvolutionContext<'I, 'T when 'I: equality and 'I: comparison and 'T: equality and 'T: comparison> =
        {
            poissonSampler: PoissonSampler<'T>
            toDouble : 'T -> double
            fromDouble : double -> 'T
        }


    /// A type to describe a multiplier for the Poisson evolution.
    type Multiplier<'I when 'I : equality and 'I : comparison> =
        | Multiplier of ('I -> double)

        member r.invoke = let (Multiplier v) = r in v
        static member inline identity : Multiplier<'I> = Multiplier (fun _ -> 1.0)

        /// Creates a spherically symmetric multiplier by projecting the input to coordinates
        /// and calculating the radius, then applying a function to the radius.
        static member inline sphericallySymmetric (converter : ConversionParameters<'I, 'C>) (radiusFunc : double -> double) : Multiplier<'I> =
                Multiplier (fun i ->
                    let coord = converter.projector i
                    let rSquared = converter.arithmetic.dot coord coord
                    radiusFunc rSquared
                )

        /// Evolve a SparseArray by a given multiplier.
        /// Only positive values are considered for evolution.
        member inline m.evolveStep (p : EvolutionContext<'I, 'T>) (array : SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
            let g (v : SparseValue<'I, 'T>) =
                if v.value > LanguagePrimitives.GenericZero<'T> then
                    let poissonSampler = p.poissonSampler
                    let valueAsDouble = p.toDouble v.value
                    let lambda = m.invoke v.x
                    let lambda_scaled = valueAsDouble * lambda
                    let moving_elements = poissonSampler.next lambda_scaled

                    Some { x = v.x; value = moving_elements }
                else None

            array.getValues()
            |> Seq.map g
            |> Seq.choose id
            |> Seq.toArray
            |> SparseArray.create


    /// A type to describe Poisson evolution.
    type EvolutionMatrix<'I when ^I: equality and ^I: comparison> =
        {
            multiplier : Multiplier<'I>
            evolutionMatrix : SparseMatrix<'I, double>
        }

        /// Evolves a sparse array using random walk probabilities.
        /// p: evolution context.
        /// m: evolution matrix containing functions of transition probabilities and a scaling factor.
        /// c: global scaling factor.
        /// array: the sparse array to evolve.
        member inline em.evolveStep (p : EvolutionContext<'I, 'T>) (c : double) (array : SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
            let result = Dictionary<'I, 'T>()
            let getMultiplier = em.multiplier.invoke
            let y_x = em.evolutionMatrix.y_x

            // For each point y in the sparse array (source location)
            for sparseValue in array.getValues() do
                let y = sparseValue.x
                let y_value = sparseValue.value

                // Skip if there are no elements at this location
                if y_value > LanguagePrimitives.GenericZero<'T> then
                    // Get the Poisson sampler and scaling factor for this position
                    let poissonSampler = p.poissonSampler
                    let multiplier = getMultiplier y

                    // Get all transition probabilities from this position y to other positions
                    let x_values = y_x y

                    // For each possible destination x
                    for x_sparse_value in x_values.getValues() do
                        let x = x_sparse_value.x
                        let transition_prob = x_sparse_value.value

                        // Calculate lambda for the Poisson distribution based on:
                        // - Number of elements at source (y_value).
                        // - Transition probability (transition_prob).
                        // - Normalized Scaling factor for this position.
                        // - Global scaling factor (c).
                        let valueAsDouble = p.toDouble y_value
                        let lambda = transition_prob * multiplier * c
                        let lambda_scaled = valueAsDouble * lambda

                        // Sample from Poisson distribution to get number of elements moving from y to x
                        let moving_elements = poissonSampler.next lambda_scaled

                        // Only add to result if we have elements moving
                        if moving_elements > LanguagePrimitives.GenericZero<'T> then
                            // Add to result
                            if result.ContainsKey(x) then result[x] <- result[x] + moving_elements
                            else result.Add(x, moving_elements)

            // Convert the result dictionary to a SparseArray
            result
            |> Seq.map (fun kvp -> { x = kvp.Key; value = kvp.Value })
            |> Seq.toArray
            |> SparseArray.create

        // /// Evolve a SparseArray by a given multiplier.
        // /// Only positive values are considered for evolution.
        // member inline _.evolve (p : EvolutionContext<'I, 'T>) (m : Multiplier<'I>) (array : SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
        //     let g (v : SparseValue<'I, 'T>) =
        //         if v.value > LanguagePrimitives.GenericZero<'T> then
        //             let poissonSampler = p.poissonSampler
        //             let valueAsDouble = p.toDouble v.value
        //             let lambda = m.invoke v.x
        //             let lambda_scaled = valueAsDouble * lambda
        //             let moving_elements = poissonSampler.next lambda_scaled
        //
        //             Some { x = v.x; value = moving_elements }
        //         else None
        //
        //     array.getValues()
        //     |> Seq.map g
        //     |> Seq.choose id
        //     |> Seq.toArray
        //     |> SparseArray.create


    // type SparseArray<'I, 'T
    //         when ^I: equality
    //         and ^I: comparison
    //         and ^T: (static member ( * ) : ^T * ^T -> ^T)
    //         and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //         and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //         and ^T: (static member Zero : ^T)
    //         and ^T: equality
    //         and ^T: comparison>
    //     with
    //
    //     /// Evolves a sparse array using random walk probabilities.
    //     /// p: evolution context.
    //     /// m: evolution matrix containing functions of transition probabilities and a scaling factor.
    //     /// array: the sparse array to evolve.
    //     member inline array.evolve (p : EvolutionContext<'I, 'T>, m : EvolutionMatrix<'I>, c : double) : SparseArray<'I, 'T> =
    //         let result = Dictionary<'I, 'T>()
    //         let getMultiplier = m.multiplier.invoke
    //         let y_x = m.evolutionMatrix.y_x
    //
    //         // For each point y in the sparse array (source location)
    //         for sparseValue in array.getValues() do
    //             let y = sparseValue.x
    //             let y_value = sparseValue.value
    //
    //             // Skip if there are no elements at this location
    //             if y_value > LanguagePrimitives.GenericZero<'T> then
    //                 // Get the Poisson sampler and scaling factor for this position
    //                 let poissonSampler = p.getPoissonSampler y
    //                 let multiplier = getMultiplier y
    //
    //                 // Get all transition probabilities from this position y to other positions
    //                 let x_values = y_x y
    //
    //                 // For each possible destination x
    //                 for x_sparse_value in x_values.getValues() do
    //                     let x = x_sparse_value.x
    //                     let transition_prob = x_sparse_value.value
    //
    //                     // Calculate lambda for the Poisson distribution based on:
    //                     // - Number of elements at source (y_value).
    //                     // - Transition probability (transition_prob).
    //                     // - Normalized Scaling factor for this position.
    //                     // - Global scaling factor (c).
    //                     let valueAsDouble = p.toDouble y_value
    //                     let lambda = transition_prob * multiplier * c
    //                     let lambda_scaled = valueAsDouble * lambda
    //
    //                     // Sample from Poisson distribution to get number of elements moving from y to x
    //                     let moving_elements = poissonSampler lambda_scaled
    //
    //                     // Only add to result if we have elements moving
    //                     if moving_elements > LanguagePrimitives.GenericZero<'T> then
    //                         // Add to result
    //                         if result.ContainsKey(x) then
    //                             result[x] <- result[x] + moving_elements
    //                         else
    //                             result.Add(x, moving_elements)
    //
    //         // Convert the result dictionary to a SparseArray
    //         result
    //         |> Seq.map (fun kvp -> { x = kvp.Key; value = kvp.Value })
    //         |> Seq.toArray
    //         |> SparseArray.create
    //
    //     /// Evolve a SparseArray by a given multiplier.
    //     /// Only positive values are considered for evolution.
    //     member inline array.evolve (p : EvolutionContext<'I, 'T>, m : Multiplier<'I>) : SparseArray<'I, 'T> =
    //         let g (v : SparseValue<'I, 'T>) =
    //             if v.value > LanguagePrimitives.GenericZero<'T> then
    //                 let poissonSampler = p.getPoissonSampler v.x
    //                 let valueAsDouble = p.toDouble v.value
    //                 let lambda = m.invoke v.x
    //                 let lambda_scaled = valueAsDouble * lambda
    //                 let moving_elements = poissonSampler lambda_scaled
    //
    //                 Some { x = v.x; value = moving_elements }
    //             else None
    //
    //         array.getValues()
    //         |> Seq.map g
    //         |> Seq.choose id
    //         |> Seq.toArray
    //         |> SparseArray.create


    // /// TODO kk:20231017 - Only scalar eps is supported for now.
    // /// Type to describe a function used to calculate eps in mutation probability calculations.
    // type EpsFunc =
    //     | EpsFunc of (Domain -> double -> double)
    //
    //     member r.invoke = let (EpsFunc v) = r in v
    //
    //
    // type Eps0 =
    //     | Eps0 of double
    //
    //     member r.value = let (Eps0 v) = r in v
    //     static member defaultValue = Eps0 0.01
    //     static member defaultNarrowValue = Eps0 0.005
    //     static member defaultWideValue = Eps0 0.02
    //     // member e.modelString = toModelString Eps0.defaultValue.value e.value |> bindPrefix "e"
    //
    //
    // type EpsFuncValue =
    //     | ScalarEps of Eps0
    //
    //     member ef.epsFunc (_ : Domain) : EpsFunc =
    //         match ef with
    //         | ScalarEps e -> EpsFunc (fun _ _ -> e.value)
    //
    //
    // type ProbabilityParams =
    //     {
    //         domainParams : DomainParams
    //         zeroThreshold : ZeroThreshold<double>
    //         maxIndexDiff : int option
    //         epsFuncValue : EpsFuncValue
    //     }
