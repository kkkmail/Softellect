namespace Softellect.Samples.DistrProc.Core

open System
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


    let inputParams =
        {
            startTime = EvolutionTime 0m
            endTime = EvolutionTime 1_000m
        }

    let outputParams =
        {
            noOfOutputPoints = 4_000
            noOfProgressPoints = 100
            noOfChartDetailedPoints = Some 20
        }


    /// That's 'I in the type signature.
    type TestInitialData =
        {
            seedValue : int
        }


    /// That's 'D in the type signature.
    type TestSolverData =
        {
            derivativeCalculator : DerivativeCalculator
            initialValues : double[]
            chartLabels : string[]
        }

        member d.odeParams =
            {
                stepSize = 0.0
                absoluteTolerance = AbsoluteTolerance.defaultValue
                odeSolverType = OdePack (Bdf, ChordWithDiagonalJacobian, UseNonNegative correctionValue)
                derivative = d.derivativeCalculator
            }


    type DampedHarmonicOscillatorData =
        {
            k: double
            c: double
        }

        member d.derivativeCalculator = DerivativeCalculator.dampedHarmonicOscillator d.k d.c

        static member create i =
            let rnd = Random(i.seedValue)

            {
                derivativeCalculator =
                    {
                        k = 1.0 + (rnd.NextDouble() - 0.5) * 0.1
                        c = 0.1 + (rnd.NextDouble() - 0.5) * 0.01
                    }.derivativeCalculator
                initialValues = [| 10.0 + (rnd.NextDouble() - 0.5) * 1.0; 10.0 + (rnd.NextDouble() - 0.5) * 1.0 |]
                chartLabels = [| "Velocity"; "Acceleration" |]
            }


    type LorenzSystemData =
        {
            sigma : double
            rho : double
            beta : double
        }

        member d.derivativeCalculator = DerivativeCalculator.lorenzSystem d.sigma d.rho d.beta

        static member create i =
            let rnd = Random(i.seedValue)

            {
                derivativeCalculator =
                    {
                        sigma = 10.0 + (rnd.NextDouble() - 0.5) * 1.0
                        rho = 28.0 + (rnd.NextDouble() - 0.5) * 2.0
                        beta = (8.0 / 3.0) + (rnd.NextDouble() - 0.5) * 0.1
                    }.derivativeCalculator
                initialValues = [| 10.0 + (rnd.NextDouble() - 0.5) * 1.0; 10.0 + (rnd.NextDouble() - 0.5) * 1.0; 10.0 + (rnd.NextDouble() - 0.5) * 1.0 |]
                chartLabels = [| "x"; "y"; "z" |]
            }


    type LotkaVolterraData =
        {
            alpha : double
            beta : double
            gamma : double
            delta : double
        }

        member d.derivativeCalculator = DerivativeCalculator.lotkaVolterra d.alpha d.beta d.gamma d.delta

        static member create i =
            let rnd = Random(i.seedValue)

            {
                derivativeCalculator =
                    {
                        alpha = 2.0 / 3.0 + (rnd.NextDouble() - 0.5) * 0.1
                        beta = 4.0 / 3.0 + (rnd.NextDouble() - 0.5) * 0.1
                        gamma = 1.0 + (rnd.NextDouble() - 0.5) * 0.1
                        delta = 1.0 + (rnd.NextDouble() - 0.5) * 0.1
                    }.derivativeCalculator
                initialValues = [| 10.0 + (rnd.NextDouble() - 0.5) * 1.0; 10.0 + (rnd.NextDouble() - 0.5) * 1.0 |]
                chartLabels = [| "Prey"; "Predator" |]
            }


    type TestDerivativeData =
        | DampedHarmonicOscillator of DampedHarmonicOscillatorData
        | LorenzSystem of LorenzSystemData
        | LotkaVolterra of LotkaVolterraData

        member d.derivativeCalculator =
            match d with
            | DampedHarmonicOscillator d -> d.derivativeCalculator
            | LorenzSystem d -> d.derivativeCalculator
            | LotkaVolterra d -> d.derivativeCalculator

        static member create i = LotkaVolterraData.create i


    type TestSolverData
        with
        static member create i = LotkaVolterraData.create i

    // type TestSolverData =
    //     {
    //         derivativeData : TestDerivativeData
    //         initialValues : double[]
    //     }
    //
    //     member d.derivativeCalculator = d.derivativeData.derivativeCalculator
    //
    //     member d.odeParams =
    //         {
    //             stepSize = 0.0
    //             absoluteTolerance = AbsoluteTolerance.defaultValue
    //             odeSolverType = OdePack (Bdf, ChordWithDiagonalJacobian, UseNonNegative correctionValue)
    //             derivative = d.derivativeCalculator
    //         }
    //
    //     static member create i =
    //         let rnd = Random(i.seedValue)
    //
    //         {
    //             derivativeData =
    //                 {
    //                     sigma = 10.0 + (rnd.NextDouble() - 0.5) * 1.0
    //                     rho = 28.0 + (rnd.NextDouble() - 0.5) * 2.0
    //                     beta = (8.0 / 3.0) + (rnd.NextDouble() - 0.5) * 0.1
    //                 }
    //             initialValues = [| 10.0 + (rnd.NextDouble() - 0.5) * 1.0; 10.0 + (rnd.NextDouble() - 0.5) * 1.0; 10.0 + (rnd.NextDouble() - 0.5) * 1.0 |]
    //         }


    /// That's 'P in the type signature.
    type TestProgressData =
        {
            x : int
        }


    /// That's 'C in the type signature.
    type TestChartData =
        {
            t : double
            x : double[]
        }
