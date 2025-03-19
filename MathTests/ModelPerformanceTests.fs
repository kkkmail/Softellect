namespace Softellect.Tests.MathTests

open Softellect.Math.Primitives
open Softellect.Math.Sparse
open Softellect.Math.Tridiagonal
open Softellect.Math.Evolution
open Softellect.Math.Models
open Xunit
open Xunit.Abstractions
open FluentAssertions
open System
open System.Diagnostics

module Helpers =
    let inline runEvolutionModelTest<'I, 'C, 'D
            when ^I: equality
            and ^I: comparison
            and ^I: (member toCoord : ^D -> ^C)

            and ^C: equality
            and ^C: comparison
            and ^C: (static member ( + ) : ^C * ^C -> ^C)
            and ^C: (static member ( - ) : ^C * ^C -> ^C)
            and ^C: (static member ( * ) : ^C * ^C -> ^C)
            and (^C or double): (static member ( .* ) : double * ^C -> ^C)
            and (^C or double): (static member ( *. ) : ^C * double -> ^C)
            and ^C: (static member ( / ) : ^C * ^C -> ^C)
            and (^C or double): (static member ( /. ) : ^C * double -> ^C)
            and ^C: (static member ( ** ) : ^C * ^C -> double)
            and ^C: ( member total : unit -> double)
            and ^C: ( member sqrt : unit -> ^C)
            and ^C: (static member Zero : ^C)
            and ^C: (static member One : ^C)
            and ^D : (member dimension : int)>
            (name: string)
            writeLine
            (createDomain: DomainIntervals -> DomainRange -> 'D)
            (createCenterPoint: int -> 'I)
            (createTridiagonalMatrix: int -> float -> SparseMatrix<'I, float>) =

        // Measure execution time
        let stopwatch = Stopwatch.StartNew()

        let noOfEpochs = NoOfEpochs 1_000_000
        let domainIntervals = DomainIntervals 1000
        let domain = createDomain domainIntervals DomainRange.defaultValue

        let dimension = domain.dimension
        writeLine($"Domain created with {domainIntervals.value}^{dimension} intervals")

        // Create model parameters
        let a = 0.99 // Parameter for tridiagonal matrix

        // Create the evolution matrix
        let d = domainIntervals.value // Number of points in domain
        let evolutionMatrix = createTridiagonalMatrix d a

        // Create multiplier for evolution matrix - spherically symmetric function 0.1 * (1.0 + 1.0 * r^2)
        let evolutionMultiplier =
            Multiplier.sphericallySymmetric<'C>
                (fun (p : 'I) -> p.toCoord domain)
                (fun rSquared -> 0.1 * (1.0 + 1.0 * rSquared))

        // Create the evolution matrix wrapper
        let evolution =
            {
                multiplier = evolutionMultiplier
                evolutionMatrix = evolutionMatrix
            }

        // Create decay function - spherically symmetric function 0.01 * (1.0 + ((3.0/2.0)*r)^8 / 8!)
        let factorial8 = 40320.0 // 8!
        let decay =
            Multiplier.sphericallySymmetric<'C>
                (fun (p : 'I) -> p.toCoord domain)
                (fun rSquared ->
                    let r = Math.Sqrt(rSquared)
                    let term = Math.Pow(1.5 * r, 8.0) / factorial8
                    0.01 * (1.0 + term))

        // Create the model
        let model =
            {
                replication = evolution
                decay = decay
                recyclingRate = RecyclingRate 1.0
                numberOfMolecules = NumberOfMolecules 1
                domain = domain
            }

        // Create random for Poisson sampler
        let random = Random(1) // Fixed seed for reproducibility
        let poissonSampler : PoissonSampler<int64> = PoissonSampler.create random

        // Create evolution context
        let evolutionContext =
            {
                poissonSampler = poissonSampler
                toDouble = double
                fromDouble = int64
            }

        // Create initial substance data
        // Center point in domain
        let centerPoint = createCenterPoint (domainIntervals.value / 2)

        let protocells = 1000L

        // Create sparse array with 1000 at the center
        let initialProtocells = [| { x = centerPoint; value = protocells } |] |> SparseArray.create

        let initialSubstanceData =
            {
                food = FoodData (1000000000L - protocells)
                waste = WasteData 0L
                protocell = ProtoCellData initialProtocells
            }

        writeLine("Initial model and data created")
        writeLine($"Initial protocells: {initialProtocells.total()}")

        let invariant0 = model.invariant initialSubstanceData

        // Evolve the model
        writeLine($"Starting evolution for {noOfEpochs.value} epochs...")
        let setupTime = stopwatch.ElapsedMilliseconds

        stopwatch.Restart()
        let result = model.evolve evolutionContext noOfEpochs initialSubstanceData
        let evolutionTime = stopwatch.ElapsedMilliseconds

        // Display results
        let finalProtocells = result.protocell.value
        let totalProtocells = finalProtocells.total()

        let invariant = model.invariant result
        let mean = model.mean result
        let stdDev = model.stdDev result

        writeLine($"{name} results:")
        writeLine($"Setup time: {setupTime} ms")
        writeLine($"Evolution time: {evolutionTime} ms")
        writeLine($"Time per epoch: {double evolutionTime / double noOfEpochs.value:F2} ms")

        writeLine($"Final food: {result.food.value}")
        writeLine($"Final waste: {result.waste.value}")
        writeLine($"Final total protocells: {totalProtocells}")
        writeLine($"Final count of different protocells: {finalProtocells.values.Length}")

        writeLine($"initial invariant: {invariant0}")
        writeLine($"final invariant: {invariant}")
        writeLine($"final mean: %A{mean}")
        writeLine($"final stdDev: %A{stdDev}")

        // Report memory usage if possible
        let currentProcess = Process.GetCurrentProcess()
        let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)
        writeLine($"Approximate memory usage: {memoryMB} MB")

        // Find max count and its location
        let mutable maxCount = 0L
        let mutable maxLocation = Unchecked.defaultof<'I>

        finalProtocells.values
        |> Array.iter (fun item ->
            if item.value > maxCount then
                maxCount <- item.value
                maxLocation <- item.x
        )

        writeLine($"Max protocell count: {maxCount} at location {maxLocation}")

        // Simple assertions to verify the test ran correctly
        totalProtocells.Should().BeGreaterThan(0L, "total number of protocells should be positive") |> ignore
        invariant.Should().Be(invariant0, "invariant should be preserved") |> ignore

open Helpers

type ModelPerformanceTests(output: ITestOutputHelper) =
    let writeLine = output.WriteLine

    [<Fact>]
    member _.``Evolution model performance test 2D`` () =
        runEvolutionModelTest<Point2D, Coord2D, Domain2D>
            "2D Model"
            writeLine
            (fun i r -> Domain2D.create(i, r))
            (fun mid -> { i0 = mid; i1 = mid })
            createTridiagonalMatrix2D

    [<Fact>]
    member _.``Evolution model performance test 3D`` () =
        runEvolutionModelTest<Point3D, Coord3D, Domain3D>
            "3D Model"
            writeLine
            (fun i r -> Domain3D.create(i, r))
            (fun mid -> { i0 = mid; i1 = mid; i2 = mid })
            createTridiagonalMatrix3D
