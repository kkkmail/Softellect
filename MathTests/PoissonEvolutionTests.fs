namespace Softellect.Tests.MathTests

open System
open Xunit
open FluentAssertions
open Softellect.Math.Primitives
open Softellect.Math.Sparse
open Softellect.Math.Tridiagonal
open Softellect.Math.Evolution
open Xunit.Abstractions

type PoissonEvolutionTests(output: ITestOutputHelper) =

    [<Fact>]
    member _.``Evolution should distribute elements according to transition probabilities in 2D``() =
        // Arrange
        let d = 5 // 5x5 grid
        let a = 0.4 // Probability of staying
        let matrix = createTridiagonalMatrix2D d a

        // Create a sparse array with a single point in the middle
        let centerPoint = { i0 = 2; i1 = 2 }
        let initialValue = 100L // Start with 100 elements
        let initialArray = SparseArray.create [| { x = centerPoint; value = initialValue } |]

        // // Create a deterministic Poisson sampler for testing
        // // This simply returns the lambda value rounded to the nearest integer
        // let deterministicPoissonSampler (lambda: double) : int64 =
        //     int64 (Math.Round(lambda))
        let poissonSampler = PoissonSampler<int64>.deterministic()

        let getMultiplier = fun _ -> 1.0 // No scaling

        // Create evolution parameter
        let evolutionParam =
            {
                // poissonSampler = PoissonSampler deterministicPoissonSampler
                poissonSampler = poissonSampler
                toDouble = fun (n: int64) -> double n
                fromDouble = fun (d: double) -> int64 d
            }

        let evolutionMatrix =
            {
                multiplier = Multiplier getMultiplier
                evolutionMatrix = matrix
            }

        // Act
        let evolvedArray = evolutionMatrix.evolveStep evolutionParam 1.0 initialArray

        // Assert
        // Get all values in the evolved array
        let values = evolvedArray.getValues()

        // Get the expected distributions based on the tridiagonal matrix values
        let centerTransitions = matrix.y_x centerPoint
        let transitionValues = centerTransitions.getValues()

        // Expected values based on deterministic sampling and the transition probabilities
        let expectedValues =
            transitionValues
            |> Seq.map (fun tv ->
                let lambda = (double initialValue) * tv.value
                // let expectedCount = deterministicPoissonSampler lambda
                let expectedCount = poissonSampler.nextNumberOfEvents lambda
                (tv.x, expectedCount))
            |> Map.ofSeq

        // Verify the total number of elements is conserved (approximately)
        let totalElements = values |> Seq.sumBy (fun v -> v.value)
        totalElements.Should().BeInRange(initialValue - 5L, initialValue + 5L) |> ignore

        // Output detailed information
        output.WriteLine($"Initial elements: {initialValue}")
        output.WriteLine($"Total elements after evolution: {totalElements}")
        output.WriteLine("Expected distribution:")

        for (point, count) in Map.toArray expectedValues do
            output.WriteLine($"Point {point}: {count}")

        output.WriteLine("Actual distribution:")
        for value in values do
            output.WriteLine($"Point {value.x}: {value.value}")

        // Check that each point has approximately the expected number of elements
        // (allowing for some rounding differences)
        for value in values do
            let point = value.x
            let actualCount = value.value

            if expectedValues.ContainsKey(point) then
                let expectedCount = expectedValues[point]
                actualCount.Should().BeInRange(expectedCount - 1L, expectedCount + 1L) |> ignore
                output.WriteLine($"Point {point}: Expected {expectedCount}, Got {actualCount}")
            else
                // This point shouldn't have any elements - this is a failure case
                actualCount.Should().Be(0L) |> ignore
                output.WriteLine($"Unexpected point {point} with {actualCount} elements")

        // Check that all expected points are present in the result
        for point in Map.keys expectedValues do
            // Skip points with zero expected elements
            if expectedValues[point] > 0L then
                let hasPoint = values |> Seq.exists (fun v -> v.x = point)
                hasPoint.Should().BeTrue() |> ignore
                if not hasPoint then
                    output.WriteLine($"Missing expected point {point} with {expectedValues[point]} elements")
