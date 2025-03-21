namespace Softellect.Tests.MathTests

open System
open System.Diagnostics
open Xunit
open Xunit.Abstractions
open FluentAssertions
open Softellect.Math.Primitives

module SparsePerformance =

    type OldSparseArray2D = Softellect.Math.Sparse.SparseArray<Point2D, int64>
    type NewSparseArray2D = Softellect.Math.Sparse2.SparseArray<Point2D, int64>

    type SparseArrayMultiplicationTests(output: ITestOutputHelper) =
        let multiplySparseArrays = Softellect.Math.Sparse.multiplySparseArrays

        let createRandomSparseArray2D (size: int) (density: float) (random: Random) =
            // Create a random 2D sparse array with the specified density
            let createElements converter =
                [|
                    for _ in 0 .. (int (float size * float size * density)) - 1 do
                        let x = random.Next(size)
                        let y = random.Next(size)
                        let point = { i0 = x; i1 = y }
                        let value = random.Next(1, 100) |> int64  // Using int64 values
                        yield (point, value)
                |]
                |> Array.distinctBy fst
                |> Array.map converter

            // Create using the same random seed but separate element generation to avoid bias
            let oldElements : Softellect.Math.Sparse.SparseValue<Point2D, int64>[] = createElements (fun (point, value) -> { x = point; value = value })
            let newElements : Softellect.Math.Sparse2.SparseValue<Point2D, int64>[] = createElements (fun (point, value) -> { x = point; value = value })

            let oldArray = OldSparseArray2D.create oldElements
            let newArray = NewSparseArray2D.create (fun e -> e > 0L) newElements

            (oldArray, newArray)

        let createRandomSparseArrays2D (count: int) (size: int) (density: float) =
            let random = Random(42) // Use the same seed for reproducibility
            [| for _ in 1 .. count do yield createRandomSparseArray2D size density random |]

        [<Fact>]
        let ``Compare performance of sparse array multiplication in 2D``() =
            // Parameters for the test
            let arrayCount = 2
            let size = 1000
            let density = 0.5 // 50% of elements are non-zero

            output.WriteLine($"Testing multiplication of {arrayCount} sparse arrays of size {size}x{size} with density {density}")

            // Create random sparse arrays
            let arraySets = createRandomSparseArrays2D arrayCount size density

            // Extract old and new arrays
            let oldArrays = arraySets |> Array.map fst |> List.ofArray
            let newArrays = arraySets |> Array.map snd |> List.ofArray

            // Create a single stopwatch for both tests
            let stopwatch = Stopwatch()

            // Benchmark the old implementation
            stopwatch.Restart()
            let oldResult = multiplySparseArrays oldArrays
            stopwatch.Stop()
            let oldElapsed = stopwatch.Elapsed

            // Benchmark the new implementation
            stopwatch.Restart()
            // Use the pre-defined arithmeticOperations for int64
            let newResult = NewSparseArray2D.multiply arithmeticOperationsInt64 newArrays
            stopwatch.Stop()
            let newElapsed = stopwatch.Elapsed

            // Output the results
            output.WriteLine($"Old implementation took {oldElapsed.TotalMilliseconds} ms")
            output.WriteLine($"New implementation took {newElapsed.TotalMilliseconds} ms")
            output.WriteLine($"Speedup factor: {oldElapsed.TotalMilliseconds / newElapsed.TotalMilliseconds}")

            // Verify that both implementations produce identical results with int64

            let oldCount = oldResult.values.Length
            let newCount = newResult.values.Length

            output.WriteLine($"Old result has {oldCount} non-zero elements")
            output.WriteLine($"New result has {newCount} non-zero elements")

            // With int64, we expect exact matches
            oldCount.Should().Be(newCount, "Both implementations should produce the same number of non-zero elements") |> ignore

            // Compare the sums to ensure they're the same
            let oldSum = oldResult.values |> Array.sumBy _.value
            let newSum = newResult.values |> Array.sumBy _.value

            output.WriteLine($"Old result sum: {oldSum}")
            output.WriteLine($"New result sum: {newSum}")

            // With int64, the sums should be exactly equal
            oldSum.Should().Be(newSum, "Both implementations should produce identical sums") |> ignore

            // For a more thorough check, we could sort the elements and compare them directly
            let oldSorted = oldResult.values |> Array.sortBy _.x
            let newSorted = newResult.values |> Array.sortBy _.x

            for i in 0 .. oldSorted.Length - 1 do
                let oldValue = oldSorted[i]
                let newValue = newSorted[i]

                oldValue.x.i0.Should().Be(newValue.x.i0, $"Coordinate i0 at index {i} should match") |> ignore
                oldValue.x.i1.Should().Be(newValue.x.i1, $"Coordinate i1 at index {i} should match") |> ignore
                oldValue.value.Should().Be(newValue.value, $"Value at index {i} should match") |> ignore

        // You might want to add more tests for other dimensions or operations
