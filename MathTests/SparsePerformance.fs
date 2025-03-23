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

        let createRandomSparseArrays2D (count: int) (size: int) (density: float) =
            // Single random generator with fixed seed
            let random = Random(1)

            // Create both arrays with exactly the same elements
            let arrays =
                [|
                    for _ in 1 .. count do
                        // Generate the elements once
                        let elements =
                            [|
                                for _ in 0 .. (int (float size * float size * density)) - 1 do
                                    let x = random.Next(size)
                                    let y = random.Next(size)
                                    let point = { i0 = x; i1 = y }
                                    let value = random.Next(1, 100) |> int64
                                    yield (point, value)
                            |]
                            |> Array.distinctBy fst

                        // Create both arrays with the same elements
                        let oldElements = elements |> Array.map (fun (point, value) ->
                            { x = point; value = value } : Softellect.Math.Sparse.SparseValue<Point2D, int64>)

                        let newElements = elements |> Array.map (fun (point, value) ->
                            { x = point; value = value } : Softellect.Math.Sparse2.SparseValue<Point2D, int64>)

                        let oldArray = OldSparseArray2D.create oldElements
                        let newArray = NewSparseArray2D.create (fun e -> e > 0L) newElements

                        yield (oldArray, newArray)
                |]

            arrays

        [<Fact>]
        let ``Compare performance of sparse array multiplication in 2D``() =
            // Parameters for the test
            let arrayCount = 4
            let size = 1000
            let density = 0.6 // 60% of elements are non-zero

            output.WriteLine($"Testing multiplication of {arrayCount} sparse arrays of size {size}x{size} with density {density}")

            // Create random sparse arrays
            let arraySets = createRandomSparseArrays2D arrayCount size density

            // Extract old and new arrays
            let oldArrays = arraySets |> Array.map fst |> List.ofArray
            let newArrays = arraySets |> Array.map snd |> List.ofArray

            // Output initial array sizes to verify they match
            for i in 0 .. arrayCount - 1 do
                output.WriteLine($"Array {i+1}: Old has {oldArrays[i].values.Length} elements, New has {newArrays[i].values.Length} elements")

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

        [<Fact>]
        let ``Compare performance of tridiagonal matrix by sparse array multiplication in 2D``() =
            // Parameters for the test
            let size = 1000
            let alpha = 0.5
            let arrayDensity = 0.4

            output.WriteLine($"Testing multiplication of tridiagonal matrix size {size}x{size} with alpha {alpha} by sparse array with density {arrayDensity}")

            // Create tridiagonal matrices using both implementations
            let oldMatrix = Softellect.Math.Tridiagonal.createTridiagonalMatrix2D size alpha
            let newMatrix = Softellect.Math.Tridiagonal2.createTridiagonalMatrix2D size alpha

            // Create a sparse array for multiplication (convert from int64 to double)
            let (oldArrayInt64, newArrayInt64) = createRandomSparseArrays2D 1 size arrayDensity |> Array.head

            // Convert array values from int64 to double
            let convertToDouble (array: Softellect.Math.Sparse.SparseArray<Point2D, int64>) =
                let doubleValues = array.values |> Array.map (fun sv ->
                    { x = sv.x; value = float sv.value } : Softellect.Math.Sparse.SparseValue<Point2D, float>)
                Softellect.Math.Sparse.SparseArray.create doubleValues

            let convertToDouble2 (array: Softellect.Math.Sparse2.SparseArray<Point2D, int64>) =
                let doubleValues = array.values |> Array.map (fun sv ->
                    { x = sv.x; value = float sv.value } : Softellect.Math.Sparse2.SparseValue<Point2D, float>)
                Softellect.Math.Sparse2.SparseArray.create (fun e -> e <> 0.0) doubleValues

            let oldArray = convertToDouble oldArrayInt64
            let newArray = convertToDouble2 newArrayInt64

            // Output array sizes to verify
            output.WriteLine($"Array size: Old has {oldArray.values.Length} elements, New has {newArray.values.Length} elements")

            // Create a single stopwatch for both tests
            let stopwatch = Stopwatch()

            // Benchmark the old implementation (using operator *)
            stopwatch.Restart()
            let oldResult = oldMatrix * oldArray
            stopwatch.Stop()
            let oldElapsed = stopwatch.Elapsed

            // Benchmark the new implementation (using multiply method)
            stopwatch.Restart()
            let newResult = newMatrix.multiply arithmeticOperationsDouble newArray
            stopwatch.Stop()
            let newElapsed = stopwatch.Elapsed

            // Output the results
            output.WriteLine($"Old implementation took {oldElapsed.TotalMilliseconds} ms")
            output.WriteLine($"New implementation took {newElapsed.TotalMilliseconds} ms")
            output.WriteLine($"Speedup factor: {oldElapsed.TotalMilliseconds / newElapsed.TotalMilliseconds}")

            // Verify that both implementations produce similar results (allowing for floating point differences)
            let oldCount = oldResult.values.Length
            let newCount = newResult.values.Length

            output.WriteLine($"Old result has {oldCount} non-zero elements")
            output.WriteLine($"New result has {newCount} non-zero elements")

            // For floating point, we allow small differences in element count due to precision
            let countDiff = Math.Abs(oldCount - newCount)
            output.WriteLine($"Element count difference: {countDiff}")

            // If the counts differ significantly, we should investigate
            let countThreshold = oldCount / 100 // Allow 1% difference
            countDiff.Should().BeLessThanOrEqualTo(countThreshold, "Element counts should be similar") |> ignore

            // Compare the sums to ensure they're close enough
            let oldSum = oldResult.values |> Array.sumBy _.value
            let newSum = newResult.values |> Array.sumBy _.value

            output.WriteLine($"Old result sum: {oldSum}")
            output.WriteLine($"New result sum: {newSum}")

            // For floating point, we allow small differences in sum
            let relativeError = Math.Abs(oldSum - newSum) / Math.Max(Math.Abs(oldSum), Math.Abs(newSum))
            output.WriteLine($"Relative error in sum: {relativeError}")

            relativeError.Should().BeLessThan(1e-10, "Sums should be very close") |> ignore

            // Check a random sample of coordinates to ensure values are close
            // We'll check at most 100 random coordinates from the old result
            let random = Random(1)
            let sampleSize = Math.Min(oldCount, 100)
            let oldIndices = [| 0 .. oldCount - 1 |] |> Array.sortBy (fun _ -> random.Next())

            for i in 0 .. sampleSize - 1 do
                let oldValue = oldResult.values[oldIndices[i]]

                // Try to find the same coordinate in the new result
                let matchingNewValue = newResult.values |> Array.tryFind (fun sv -> sv.x = oldValue.x)

                match matchingNewValue with
                | Some newValue ->
                    // For floating point, we check relative error
                    let pointError = Math.Abs(oldValue.value - newValue.value) / Math.Max(Math.Abs(oldValue.value), Math.Abs(newValue.value))
                    if pointError > 1e-10 then
                        output.WriteLine($"Value mismatch at {oldValue.x}: Old={oldValue.value}, New={newValue.value}, Error={pointError}")
                    pointError.Should().BeLessThan(1e-10, $"Values at {oldValue.x} should be very close") |> ignore
                | None ->
                    // If the value is very small in the old result, it might be zero in the new result due to precision
                    let isVerySmall = Math.Abs(oldValue.value) < 1e-10
                    if not isVerySmall then
                        output.WriteLine($"Missing point in new result at {oldValue.x} with value {oldValue.value}")
                    isVerySmall.Should().BeTrue($"Point {oldValue.x} should exist in new result or be very small in old result") |> ignore
