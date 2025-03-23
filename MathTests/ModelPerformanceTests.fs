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
    // let inline runEvolutionModelTest<'I, 'C, 'D
    //         when ^I: equality
    //         and ^I: comparison
    //         and ^I: (member toCoord : ^D -> ^C)
    //
    //         and ^C: equality
    //         and ^C: comparison
    //         and ^C: (static member ( + ) : ^C * ^C -> ^C)
    //         and ^C: (static member ( - ) : ^C * ^C -> ^C)
    //         and ^C: (static member ( * ) : ^C * ^C -> ^C)
    //         and (^C or double): (static member ( .* ) : double * ^C -> ^C)
    //         and (^C or double): (static member ( *. ) : ^C * double -> ^C)
    //         and ^C: (static member ( / ) : ^C * ^C -> ^C)
    //         and (^C or double): (static member ( /. ) : ^C * double -> ^C)
    //         and ^C: (static member ( ** ) : ^C * ^C -> double)
    //         and ^C: ( member total : unit -> double)
    //         and ^C: ( member sqrt : unit -> ^C)
    //         and ^C: (static member Zero : ^C)
    //         and ^C: (static member One : ^C)
    //         and ^D : (member dimension : int)>
    //         (name: string)
    //         writeLine
    //         (createDomain: DomainIntervals -> DomainRange -> 'D)
    //         (createCenterPoint: int -> 'I)
    //         (createTridiagonalMatrix: int -> float -> SparseMatrix<'I, float>) =
    let inline runEvolutionModelTest<'I, 'C, 'D when 'I: equality and 'I: comparison>
            (name : string)
            writeLine
            (createDomain : DomainIntervals -> DomainRange -> 'D)
            (createCenterPoint : int -> 'I)
            (createTridiagonalMatrix : int -> float -> SparseMatrix<'I, float>)
            (converter : 'D -> ConversionParameters<'I, 'C>)=

        // Measure execution time
        let stopwatch = Stopwatch.StartNew()

        let noOfEpochs = NoOfEpochs 100_000
        let domainIntervals = DomainIntervals 100
        let domain = createDomain domainIntervals DomainRange.defaultValue

        // let dimension = domain.dimension
        // writeLine($"Domain created with {domainIntervals.value}^{dimension} intervals")

        // Create model parameters
        let a = 0.99 // Parameter for tridiagonal matrix

        // Create the evolution matrix
        let d = domainIntervals.value // Number of points in domain
        let evolutionMatrix = createTridiagonalMatrix d a

        // Create multiplier for evolution matrix - spherically symmetric function 0.1 * (1.0 + 1.0 * r^2)
        let evolutionMultiplier =
            Multiplier.sphericallySymmetric<'C>
                (converter domain)
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
                (converter domain)
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
                converter = converter domain
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

        // Parameters for output frequency
        let outputBlocks = 100
        let outputHoursThreshold = 6.0 // 6 hours
        let earlyStageHoursThreshold = 0.25 // 15 minutes
        let earlyStageEpochs = noOfEpochs.value / outputBlocks

        // Store last output time
        let mutable lastOutputTime = DateTime.Now

        // Store start time for estimating completion
        let startTime = DateTime.Now

        let outputData i s =
            // Print separator to distinguish output blocks
            writeLine("-----------------------------------------------------")

            let currentTime = DateTime.Now
            let elapsedTime = (currentTime - startTime).TotalSeconds
            let percentComplete = float i / float noOfEpochs.value * 100.0
            // Avoid division by zero for i = 0
            let estimatedTotalTime = if i > 0 then elapsedTime / (float i / float noOfEpochs.value) else 0.0
            let estimatedTimeRemaining = estimatedTotalTime - elapsedTime

            // Output current step with completion estimate
            writeLine($"Step {i}/{noOfEpochs.value} ({percentComplete:F2}%%) - Est. remaining: {TimeSpan.FromSeconds(estimatedTimeRemaining)}")

            // Output relevant data for the current step
            let invariant = model.invariant s
            let mean = model.mean s
            let stdDev = model.stdDev s

            writeLine($"Current food: {s.food.value}")
            writeLine($"Current waste: {s.waste.value}")

            let currentProtocells = s.protocell.value
            let totalProtocells = currentProtocells.total()

            writeLine($"Current total protocells: {totalProtocells}")
            writeLine($"Current count of different protocells: {currentProtocells.values.Length}")
            writeLine($"Current invariant: {invariant}")
            writeLine($"Current mean: %A{mean}")
            writeLine($"Current stdDev: %A{stdDev}")

            // Find max count and its location
            let mutable maxCount = 0L
            let mutable maxLocation = Unchecked.defaultof<'I>

            // currentProtocells.values
            // |> Array.iter (fun item ->
            //     if item.value > maxCount then
            //         maxCount <- item.value
            //         maxLocation <- item.x
            // )

            writeLine($"Current max protocell count: {maxCount} at location {maxLocation}")

            // Report memory usage
            let currentProcess = Process.GetCurrentProcess()
            let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)
            writeLine($"Approximate memory usage: {memoryMB} MB")

            // Update last output time
            lastOutputTime <- currentTime

        let callBack i s =
            let currentTime = DateTime.Now
            let timeSinceLastOutput = currentTime - lastOutputTime

            // Determine if we should output data based on the requirements
            let epochInterval = max 1 (noOfEpochs.value / outputBlocks) // Ensure division is at least 1

            let shouldOutput =
                // Output data at regular intervals (every 1/100 of total epochs)
                i % epochInterval = 0 ||

                // For early stage, output at least every 15 minutes
                (i <= earlyStageEpochs && timeSinceLastOutput.TotalHours >= earlyStageHoursThreshold) ||

                // For later stages, output at least every 6 hours
                (i > earlyStageEpochs && timeSinceLastOutput.TotalHours >= outputHoursThreshold) ||

                // Always output the last epoch
                i = noOfEpochs.value

            if shouldOutput then
                outputData i s

        let ctx =
            {
                evolutionContext = evolutionContext
                noOfEpochs = noOfEpochs
                initialData = initialSubstanceData
                callBack = callBack
            }

        writeLine("Initial model and data created")
        writeLine($"Initial protocells: {initialProtocells.total()}")

        let invariant0 = model.invariant initialSubstanceData

        // Output initial data before evolution starts
        outputData 0 initialSubstanceData

        // Evolve the model
        writeLine($"Starting evolution for {noOfEpochs.value} epochs...")
        let setupTime = stopwatch.ElapsedMilliseconds

        stopwatch.Restart()
        let result = model.evolve ctx
        let evolutionTime = stopwatch.ElapsedMilliseconds

        // Display final results using the outputData function
        outputData noOfEpochs.value result

        // Add summary information
        writeLine("-----------------------------------------------------")
        writeLine($"{name} results summary:")
        writeLine($"Setup time: {setupTime} ms")
        writeLine($"Evolution time: {evolutionTime} ms")
        writeLine($"Time per epoch: {double evolutionTime / double noOfEpochs.value:F2} ms")

        // Display model invariant information
        writeLine($"Initial invariant: {invariant0}")
        writeLine($"Final invariant: {model.invariant result}")

        // Simple assertions to verify the test ran correctly
        let totalProtocells = result.protocell.value.total()
        totalProtocells.Should().BeGreaterThan(0L, "total number of protocells should be positive") |> ignore
        (model.invariant result).Should().Be(invariant0, "invariant should be preserved") |> ignore

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
            conversionParameters2D


    [<Fact>]
    member _.``Evolution model performance test 3D`` () =
        runEvolutionModelTest<Point3D, Coord3D, Domain3D>
            "3D Model"
            writeLine
            (fun i r -> Domain3D.create(i, r))
            (fun mid -> { i0 = mid; i1 = mid; i2 = mid })
            createTridiagonalMatrix3D
            conversionParameters3D
