namespace Softellect.Samples.DistrProc.Core

open System
open System.Threading
open Softellect.DistributedProcessing.Primitives.Common

module Primitives =

    let solverId = "6059CA79-A97E-4DAF-B7FD-75E26ED6FB3E" |> Guid.Parse |> SolverId
    let solverName = SolverName "Test"

    /// Treat all values of u less than this as zero.
    let correctionValue = 1.0e-12


    /// https://en.wikipedia.org/wiki/Harmonic_oscillator
    /// Interesting values: k = 1, c = 0.1
    let dampedHarmonicOscillator (k: double) (c: double) (t: double) (x: double[]) (i: int): double =
        match i with
        | 0 -> x.[1]                            // dx1/dt = velocity
        | 1 -> -k * x.[0] - c * x.[1]            // dx2/dt = -kx - cx (acceleration)
        | _ -> failwith "Invalid index"


    /// https://en.wikipedia.org/wiki/Lorenz_system
    /// Interesting values: sigma = 10, rho = 28, beta = 8/3
    let lorenzSystem (sigma: double) (rho: double) (beta: double) (t: double) (x: double[]) (i: int): double =
        printfn $"lorenzSystem: t = {t}."
        // Thread.Sleep(1_000_000) // Frees the derivative like forever.

        match i with
        | 0 -> sigma * (x.[1] - x.[0])               // dx1/dt = sigma * (x2 - x1)
        | 1 -> x.[0] * (rho - x.[2]) - x.[1]         // dx2/dt = x1 * (rho - x3) - x2
        | 2 -> x.[0] * x.[1] - beta * x.[2]          // dx3/dt = x1 * x2 - beta * x3
        | _ -> failwith "Invalid index"


    /// https://en.wikipedia.org/wiki/Lotka%E2%80%93Volterra_equations
    /// Interesting values: alpha = 2/3, beta = 4/3, gamma = 1, delta = 1
    let lotkaVolterra (alpha: double) (beta: double) (gamma: double) (delta: double) (t: double) (x: double[]) (i: int): double =
        match i with
        | 0 -> alpha * x.[0] - beta * x.[0] * x.[1]   // dx1/dt = alpha * x1 - beta * x1 * x2 (prey)
        | 1 -> delta * x.[0] * x.[1] - gamma * x.[1]  // dx2/dt = delta * x1 * x2 - gamma * x2 (predator)
        | _ -> failwith "Invalid index"


    type DerivativeCalculator
        with
        static member dampedHarmonicOscillator k c =
            dampedHarmonicOscillator k c |> OneByOne

        static member lorenzSystem sigma rho beta =
            lorenzSystem sigma rho beta |> OneByOne

        static member lotkaVolterra alpha beta gamma delta =
            lotkaVolterra alpha beta gamma delta |> OneByOne

        member d.delayed n =
            match n with
            | None -> d
            | Some (e : int) ->
                match d with
                | OneByOne f ->
                    let g t x i =
                        if i = 0
                        then
                            printfn $"Sleeping for {e} ms..."
                            Thread.Sleep(e)
                        f t x i
                    OneByOne g
                | FullArray f ->
                    let g t x =
                        printfn $"Sleeping for {e} ms..."
                        Thread.Sleep(e)
                        f t x
                    FullArray g


    type DampedHarmonicOscillatorData =
        {
            k: double
            c: double
        }

        member d.derivativeCalculator = DerivativeCalculator.dampedHarmonicOscillator d.k d.c


    type LorenzSystemData =
        {
            sigma : double
            rho : double
            beta : double
        }

        member d.derivativeCalculator = DerivativeCalculator.lorenzSystem d.sigma d.rho d.beta


    type LotkaVolterraData =
        {
            alpha : double
            beta : double
            gamma : double
            delta : double
        }

        member d.derivativeCalculator = DerivativeCalculator.lotkaVolterra d.alpha d.beta d.gamma d.delta


    type TestDerivativeData =
        | DampedHarmonicOscillator of DampedHarmonicOscillatorData
        | LorenzSystem of LorenzSystemData
        | LotkaVolterra of LotkaVolterraData

        member d.derivativeCalculator =
            match d with
            | DampedHarmonicOscillator d -> d.derivativeCalculator
            | LorenzSystem d -> d.derivativeCalculator
            | LotkaVolterra d -> d.derivativeCalculator


    let outputParams =
        {
            noOfOutputPoints = 4_000
            noOfProgressPoints = 100
            noOfResultDetailedPoints = Some 20
        }


    /// That's 'I in the type signature. This is just data, no functions, as it must be serializable.
    type TestInitialData =
        {
            seedValue : int
            delay : int option
            evolutionTime : EvolutionTime
            modelId : int
        }


    /// That's 'D in the type signature. This is a mix of data and functions.
    type TestSolverData =
        {
            initialData : TestInitialData
            derivativeData : TestDerivativeData
            initialValues : double[]
            chartLabels : string[]
        }

        member d.inputParams =
            {
                startTime = EvolutionTime 0m
                endTime = d.initialData.evolutionTime
            }

        member d.odeParams =
            {
                stepSize = 0.0
                absoluteTolerance = AbsoluteTolerance.defaultValue
                odeSolverType = OdePack (Bdf, ChordWithDiagonalJacobian, UseNonNegative correctionValue)
                derivative = d.derivativeData.derivativeCalculator.delayed d.initialData.delay
            }


    type DampedHarmonicOscillatorData
        with
        static member create i =
            let rnd = Random(i.seedValue)

            {
                initialData = i
                derivativeData =
                    {
                        k = 1.0 + (rnd.NextDouble() - 0.5) * 0.1
                        c = 0.1 + (rnd.NextDouble() - 0.5) * 0.01
                    }
                    |> DampedHarmonicOscillator
                initialValues = [| 10.0 + (rnd.NextDouble() - 0.5) * 1.0; 10.0 + (rnd.NextDouble() - 0.5) * 1.0 |]
                chartLabels = [| "Velocity"; "Acceleration" |]
            }


    type LorenzSystemData
        with
        static member create i =
            let rnd = Random(i.seedValue)

            {
                initialData = i
                derivativeData =
                    {
                        sigma = 10.0 + (rnd.NextDouble() - 0.5) * 1.0
                        rho = 28.0 + (rnd.NextDouble() - 0.5) * 2.0
                        beta = (8.0 / 3.0) + (rnd.NextDouble() - 0.5) * 0.1
                    }
                    |> LorenzSystem
                initialValues = [| 10.0 + (rnd.NextDouble() - 0.5) * 1.0; 10.0 + (rnd.NextDouble() - 0.5) * 1.0; 10.0 + (rnd.NextDouble() - 0.5) * 1.0 |]
                chartLabels = [| "x"; "y"; "z" |]
            }


    type LotkaVolterraData
        with
        static member create i =
            let rnd = Random(i.seedValue)

            {
                initialData = i
                derivativeData =
                    {
                        alpha = 2.0 / 3.0 + (rnd.NextDouble() - 0.5) * 0.1
                        beta = 4.0 / 3.0 + (rnd.NextDouble() - 0.5) * 0.1
                        gamma = 1.0 + (rnd.NextDouble() - 0.5) * 0.1
                        delta = 1.0 + (rnd.NextDouble() - 0.5) * 0.1
                    }
                    |> LotkaVolterra
                initialValues = [| 10.0 + (rnd.NextDouble() - 0.5) * 1.0; 10.0 + (rnd.NextDouble() - 0.5) * 1.0 |]
                chartLabels = [| "Prey"; "Predator" |]
            }


    type TestSolverData
        with
        static member create i =
            let data =
                match i.modelId with
                | 1 -> LotkaVolterraData.create i
                | 2 -> DampedHarmonicOscillatorData.create i
                | 3 -> LorenzSystemData.create i
                | _ -> failwith $"Invalid model id: {i.modelId}."

            data


    /// That's 'P in the type signature.
    type TestProgressData =
        {
            x : int
        }


    /// That's 'C in the type signature.
    type TestChartData =
        {
            x : double[]
        }
