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

type Model2DPerformanceTests(output: ITestOutputHelper) =
    let writeLine = output.WriteLine
    let createTridiagonalMatrix2D = createTridiagonalMatrix2D BoundaryConfig.ProportionalScaling


    [<Fact>]
    member _.``Evolution model performance test 2D`` () =
        // Measure execution time
        let stopwatch = Stopwatch.StartNew()

        // Set number of epochs
        let noOfEpochs = NoOfEpochs 100_000

        // Create a domain with 1000 x 1000 intervals
        let domainIntervals = DomainIntervals 100
        let domain = Domain2D.create(domainIntervals, DomainRange.defaultValue)
        let converter = conversionParameters2D domain

        writeLine($"Domain created with {domainIntervals.value}x{domainIntervals.value} intervals")

        // Create model parameters
        let a = 0.99 // Parameter for tridiagonal matrix

        // Create the evolution matrix
        let d = domainIntervals.value // Number of points in domain
        let evolutionMatrix = createTridiagonalMatrix2D d a

        // Create multiplier for evolution matrix - spherically symmetric function 0.1 * (1.0 + 1.0 * r^2)
        let evolutionMultiplier =
            Multiplier.sphericallySymmetric<Coord2D>
                converter
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
            Multiplier.sphericallySymmetric<Coord2D>
                converter
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
                converter = converter
            }

        // Create random for Poisson sampler
        let random = Random(1) // Fixed seed for reproducibility
        let poissonSampler = PoissonSampler.create int64 random

        // Create evolution context
        let evolutionContext =
            {
                poissonSampler = poissonSampler
                toDouble = double
                fromDouble = int64
            }

        // Create initial substance data
        // Center point in domain (500, 500) for 1000 x 1000 intervals
        let centerPoint = { i0 = domainIntervals.value / 2; i1 = domainIntervals.value / 2 }

        let protocells = 1000L

        // Create sparse array with 1000 at the center
        let initialProtocells = [| { x = centerPoint; value = protocells } |] |> SparseArray.create

        let initialSubstanceData =
            {
                food = FoodData (1000000000L - protocells)
                waste = WasteData 0L
                protocell = ProtoCellData initialProtocells
            }

        let ctx =
            {
                evolutionContext = evolutionContext
                noOfEpochs = noOfEpochs
                initialData = initialSubstanceData
                callBack = fun _ _ -> ()
            }

        writeLine("Initial model and data created")
        writeLine($"Initial protocells: {initialProtocells.total()}")

        let invariant0 = model.invariant initialSubstanceData

        // Evolve the model
        writeLine($"Starting evolution for {noOfEpochs.value} epochs...")
        let setupTime = stopwatch.ElapsedMilliseconds

        stopwatch.Restart()
        let result = model.evolve ctx
        let evolutionTime = stopwatch.ElapsedMilliseconds

        // Display results
        let finalProtocells = result.protocell.value
        let totalProtocells = finalProtocells.total()

        let invariant = model.invariant result
        let mean = model.mean result
        let stdDev = model.stdDev result

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
        let mutable maxLocation = Unchecked.defaultof<Point2D>

        finalProtocells.values
        |> Array.iter (fun item ->
            if item.value > maxCount then
                maxCount <- item.value
                maxLocation <- item.x
        )

        writeLine($"Max protocell count: {maxCount} at location ({maxLocation.i0}, {maxLocation.i1})")

        // Simple assertions to verify the test ran correctly
        totalProtocells.Should().BeGreaterThan(0L, "total number of protocells should be positive") |> ignore
        invariant.Should().Be(invariant0, "invariant should be preserved") |> ignore
