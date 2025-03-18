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

type Model3DPerformanceTests(output: ITestOutputHelper) =

    [<Fact>]
    member _.``Evolution model performance test 3D`` () =
        // Measure execution time
        let stopwatch = Stopwatch.StartNew()

        // Set number of epochs
        let noOfEpochs = NoOfEpochs 100_000

        // Create a domain with 1000 x 1000 x 1000 intervals
        let domainIntervals = DomainIntervals 1000
        let domain = Domain3D.create(domainIntervals, DomainRange.defaultValue)

        output.WriteLine($"Domain created with {domainIntervals.value}x{domainIntervals.value} intervals")

        // Create model parameters
        let a = 0.99 // Parameter for tridiagonal matrix

        // Create the evolution matrix
        let d = domainIntervals.value // Number of points in domain
        let evolutionMatrix = createTridiagonalMatrix3D d a

        // Create multiplier for evolution matrix - spherically symmetric function 0.1 * (1.0 + 1.0 * r^2)
        let evolutionMultiplier =
            Multiplier.sphericallySymmetric<Coord3D>
                (fun (p : Point3D) -> p.toCoord domain)
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
            Multiplier.sphericallySymmetric<Coord3D>
                (fun (p : Point3D) -> p.toCoord domain)
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
        let poissonSampler = PoissonSingleSampler.create random

        // Create evolution context
        let evolutionContext =
            {
                getPoissonSampler = fun _ -> poissonSampler.nextPoisson
                sampler = poissonSampler
                toDouble = double
                fromDouble = int64
            }

        // Create initial substance data
        // Center point in domain (500, 500, 500) for 1000 x 1000 x 1000 intervals
        let centerPoint = { i0 = 500; i1 = 500; i2 = 500 }

        let protocells = 1000L

        // Create sparse array with 1000 at the center
        let initialProtocells = [| { x = centerPoint; value = protocells } |] |> SparseArray.create

        let initialSubstanceData =
            {
                food = FoodData (1000000000L - protocells)
                waste = WasteData 0L
                protocell = ProtoCellData initialProtocells
            }

        output.WriteLine("Initial model and data created")
        output.WriteLine($"Initial protocells: {initialProtocells.total()}")

        let invariant0 = model.invariant initialSubstanceData

        // Evolve the model
        output.WriteLine($"Starting evolution for {noOfEpochs.value} epochs...")
        let setupTime = stopwatch.ElapsedMilliseconds

        stopwatch.Restart()
        let result = model.evolve(evolutionContext, initialSubstanceData, noOfEpochs)
        let evolutionTime = stopwatch.ElapsedMilliseconds

        // Display results
        let finalProtocells = result.protocell.value
        let totalProtocells = finalProtocells.total()

        let invariant = model.invariant result
        let mean = finalProtocells.mean double (fun (p : Point3D) -> p.toCoord model.domain)
        let variance = finalProtocells.variance double (fun (p : Point3D) -> p.toCoord model.domain)
        let stdDev = variance.sqrt()

        output.WriteLine($"Setup time: {setupTime} ms")
        output.WriteLine($"Evolution time: {evolutionTime} ms")
        output.WriteLine($"Time per epoch: {double evolutionTime / double noOfEpochs.value:F2} ms")

        output.WriteLine($"Final food: {result.food.value}")
        output.WriteLine($"Final waste: {result.waste.value}")
        output.WriteLine($"Final total protocells: {totalProtocells}")
        output.WriteLine($"Final count of different protocells: {finalProtocells.values.Length}")

        output.WriteLine($"initial invariant: {invariant0}")
        output.WriteLine($"final invariant: {invariant}")
        output.WriteLine($"final mean: %A{mean}")
        output.WriteLine($"final stdDev: %A{stdDev}")

        // Report memory usage if possible
        let currentProcess = Process.GetCurrentProcess()
        let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)
        output.WriteLine($"Approximate memory usage: {memoryMB} MB")

        // Find max count and its location
        let mutable maxCount = 0L
        let mutable maxLocation = Unchecked.defaultof<Point3D>

        finalProtocells.values
        |> Array.iter (fun item ->
            if item.value > maxCount then
                maxCount <- item.value
                maxLocation <- item.x
        )

        output.WriteLine($"Max protocell count: {maxCount} at location ({maxLocation.i0}, {maxLocation.i1})")

        // Simple assertions to verify the test ran correctly
        totalProtocells.Should().BeGreaterThan(0L, "total number of protocells should be positive") |> ignore
        invariant.Should().Be(invariant0, "invariant should be preserved") |> ignore
