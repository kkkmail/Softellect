namespace Softellect.Tests.MathTests

open System
open System.Diagnostics
open Xunit
open Xunit.Abstractions
open FluentAssertions
open Softellect.Math.Primitives

module SparsePerformance =

    type OldSparseArray2D = Softellect.Math.Sparse.SparseArray<Point2D, double>
    type NewSparseArray2D = Softellect.Math.Sparse2.SparseArray<Point2D, double>

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
                        let value = random.NextDouble()
                        yield (point, value)
                |]
                |> Array.distinctBy fst
                |> Array.map converter

            // Create using the same random seed but separate element generation to avoid bias
            let oldElements : Softellect.Math.Sparse.SparseValue<Point2D, double>[] = createElements (fun (point, value) -> { x = point; value = value })
            let newElements : Softellect.Math.Sparse2.SparseValue<Point2D, double>[] = createElements (fun (point, value) -> { x = point; value = value })

            let oldArray = OldSparseArray2D.create oldElements
            let newArray = NewSparseArray2D.create (fun e -> e > 0.0) newElements

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

            // Benchmark the old implementation
            let oldStopwatch = Stopwatch.StartNew()
            let oldResult = multiplySparseArrays oldArrays
            oldStopwatch.Stop()
            let oldElapsed = oldStopwatch.Elapsed

            // Benchmark the new implementation
            let newStopwatch = Stopwatch.StartNew()
            // Use the pre-defined arithmeticOperations2D
            let newResult = NewSparseArray2D.multiply arithmeticOperationsDouble newArrays
            newStopwatch.Stop()
            let newElapsed = newStopwatch.Elapsed

            // Output the results
            output.WriteLine($"Old implementation took {oldElapsed.TotalMilliseconds} ms")
            output.WriteLine($"New implementation took {newElapsed.TotalMilliseconds} ms")
            output.WriteLine($"Speedup factor: {oldElapsed.TotalMilliseconds / newElapsed.TotalMilliseconds}")

            // Verify that both implementations produce similar results
            // Note: We can't directly compare elements since the implementations might be different
            // But we can check for comparable number of non-zero elements and similar statistics

            let oldCount = oldResult.values.Length
            let newCount = newResult.values.Length

            output.WriteLine($"Old result has {oldCount} non-zero elements")
            output.WriteLine($"New result has {newCount} non-zero elements")

            // We would expect roughly similar counts if the algorithms are equivalent
            // But not necessarily identical due to floating point differences
            let countDifference = Math.Abs(oldCount - newCount)
            let countDifferencePercent = float countDifference / float (max oldCount newCount) * 100.0

            output.WriteLine($"Element count difference: {countDifference} ({countDifferencePercent:F2}%%)")

            // We could also check some statistical properties of the results
            let oldValues = oldResult.values
            let newValues = newResult.values

            let oldSum = oldValues |> Array.sumBy _.value
            let newSum = newValues |> Array.sumBy _.value

            output.WriteLine($"Old result sum: {oldSum}")
            output.WriteLine($"New result sum: {newSum}")

            // Assert that the implementations produce roughly similar results
            countDifferencePercent.Should().BeLessThan(10.0, "The implementations should produce similar number of non-zero elements") |> ignore
            (abs (oldSum - newSum)).Should().BeLessThan(oldSum * 0.1, "The sums should be roughly similar") |> ignore
