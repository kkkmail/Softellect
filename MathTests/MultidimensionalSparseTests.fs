namespace Softellect.Tests.MathTests

open System
open System.Diagnostics
open Xunit
open Xunit.Abstractions
open Softellect.Math.Sparse

module MultidimensionalSparseTests =
    /// Helper functions for creating multi-dimensional test data
    module Helpers =
        /// Map a dimension index to normalized range [-1, 1]
        let normalizeIndex (d: int) (idx: int) =
            (float idx) * 2.0 / (float (d - 1)) - 1.0

        /// Check if a point is near a hypersphere with radius r
        let isNearHypersphere (radius: float) (epsilon: float) (coords: float array) =
            let sumSquares =
                coords
                |> Array.sumBy (fun x -> x * x)

            let dist = Math.Sqrt(sumSquares)
            Math.Abs(dist - radius) <= epsilon

        /// Calculate the total size of a hypercube with dimension d and k dimensions
        let totalSize (d: int) (k: int) =
            pown (int64 d) k

        /// Get all adjacent points by changing one coordinate by +/- 1
        let getAdjacentPoints (d: int) (point: int[]) =
            let result = ResizeArray<int[]>()

            for i in 0..(point.Length - 1) do
                // Decreasing the i-th coordinate by 1
                if point[i] > 0 then
                    let newPoint = Array.copy point
                    newPoint[i] <- point[i] - 1
                    result.Add(newPoint)

                // Increasing the i-th coordinate by 1
                if point[i] < d - 1 then
                    let newPoint = Array.copy point
                    newPoint[i] <- point[i] + 1
                    result.Add(newPoint)

            result.ToArray()


        let mutable xyCallCount = 0
        let mutable xyTotalTime = 0L
        let xyStopwatch = Stopwatch()

        /// Reset the timing counters (call this before each test)
        let resetMatrixTimingCounters() =
            xyCallCount <- 0
            xyTotalTime <- 0L


        /// Note that this tridiagonal matrix does not sum to 1 on edges.
        /// As such, it cannot be treated as a probability matrix.
        /// Create a k-dimensional tridiagonal sparse matrix
        let createTridiagonalMatrix (d: int) (k: int) (a: float) : SparseMatrix<int[], float> =
            // Parameter validation
            if a < 0.0 || a > 1.0 then failwith "Parameter a must be in range [0, 1]."

            // Calculate base b for internal points based on the constraint a + 2 * k * b = 1
            let b = (1.0 - a) / (2.0 * float k)

            // Create the matrix as functions (no full instantiation)
            {
                x_y = fun point ->
                    xyStopwatch.Restart()
                    if point.Length <> k then failwith $"Point should have {k} dimensions"
                    let values = ResizeArray<SparseValue<int[], float>>()

                    // Diagonal element (self-connection)
                    values.Add({ x = Array.copy point; value = a })

                    // Off-diagonal elements (adjacent points)
                    // For each dimension, add connections to adjacent points
                    for i in 0..(point.Length - 1) do
                        // Decreasing the i-th coordinate by 1
                        if point[i] > 0 then
                            let newPoint = Array.copy point
                            newPoint[i] <- point[i] - 1
                            // values.Add({ x = newPoint; value = b / float k })
                            values.Add({ x = newPoint; value = b })

                        // Increasing the i-th coordinate by 1
                        if point[i] < d - 1 then
                            let newPoint = Array.copy point
                            newPoint[i] <- point[i] + 1
                            // values.Add({ x = newPoint; value = b / float k })
                            values.Add({ x = newPoint; value = b })

                    let retVal = SparseArray.create (values.ToArray())

                    xyStopwatch.Stop()
                    xyCallCount <- xyCallCount + 1
                    xyTotalTime <- xyTotalTime + xyStopwatch.ElapsedTicks

                    retVal

                y_x = fun point ->
                    xyStopwatch.Restart()

                    if point.Length <> k then
                        failwith $"Point should have {k} dimensions"

                    let values = ResizeArray<SparseValue<int[], float>>()

                    // Diagonal element (self-connection)
                    values.Add({ x = Array.copy point; value = a })

                    // Off-diagonal elements (adjacent points)
                    // For each dimension, add connections to adjacent points
                    for i in 0..(point.Length - 1) do
                        // Decreasing the i-th coordinate by 1
                        if point[i] > 0 then
                            let newPoint = Array.copy point
                            newPoint[i] <- point[i] - 1
                            // values.Add({ x = newPoint; value = b / float k })
                            values.Add({ x = newPoint; value = b })

                        // Increasing the i-th coordinate by 1
                        if point[i] < d - 1 then
                            let newPoint = Array.copy point
                            newPoint[i] <- point[i] + 1
                            // values.Add({ x = newPoint; value = b / float k })
                            values.Add({ x = newPoint; value = b })

                    let retVal = SparseArray.create (values.ToArray())

                    xyStopwatch.Stop()
                    xyCallCount <- xyCallCount + 1
                    xyTotalTime <- xyTotalTime + xyStopwatch.ElapsedTicks

                    retVal
            }

        /// Create a hypersphere vector in k dimensions
        let createHypersphereVector (d: int) (k: int) (radius: float) (epsilon: float) : SparseArray<int[], float> =
            let values = ResizeArray<SparseValue<int[], float>>()

            // For high dimensions, we need to be more selective about which points to include
            // to prevent explosion of vector elements
            let pointsToCheck =
                if k <= 3 then d
                else min d (max 100 (1000 / k)) // Reduce points for higher dimensions

            // Generate all points in the k-dimensional space
            let rec generatePoints (current: int list) (dimension: int) =
                if dimension = k then
                    // Convert the list to an array in reverse order (since we built it backwards)
                    let point = current |> List.rev |> List.toArray

                    // Calculate normalized coordinates
                    let normalizedCoords =
                        point |> Array.map (fun idx -> normalizeIndex d idx)

                    // Check if point is on the hypersphere
                    if isNearHypersphere radius epsilon normalizedCoords then
                        values.Add({ x = point; value = 1.0 })
                else
                    // Only check a subset of points for higher dimensions to avoid explosion
                    let stepSize = max 1 (d / pointsToCheck)
                    for i in 0 .. stepSize .. (d-1) do
                        generatePoints (i :: current) (dimension + 1)

            // Start recursive generation with empty list and dimension 0
            generatePoints [] 0

            SparseArray.create (values.ToArray())

    open Helpers

    /// Performance tests for sparse matrix operations with varying dimensions
    [<Trait("Category", "Performance")>]
    type PerformanceTests(output: ITestOutputHelper) =

        /// Run a performance test for the specified dimension
        let runPerformanceTest (d: int) (k: int) =
            let a = 0.5  // Diagonal element value
            let radius = 0.7 // Hypersphere radius
            let epsilon = 0.05 // Thickness of the hypersphere

            output.WriteLine($"Starting {k}D performance test with d={d}...")

            // Create the matrix (just defines the functions, doesn't instantiate all values)
            let stopwatch = Stopwatch.StartNew()
            let matrix = createTridiagonalMatrix d k a
            let creationTime = stopwatch.ElapsedMilliseconds

            // Create the vector
            output.WriteLine("Creating hypersphere vector...")
            stopwatch.Restart()
            let vector = createHypersphereVector d k radius epsilon
            let vectorCreationTime = stopwatch.ElapsedMilliseconds

            // Count a sample of non-zero elements (not the full matrix)
            let matrixSize = totalSize d k

            // Dynamic sampling based on dimension
            let sampleSize =
                match k with
                | 2 -> min 100 d
                | 3 -> min 10 d
                | _ -> min 5 d

            output.WriteLine($"Sampling {sampleSize}^{k} elements to estimate matrix density...")
            stopwatch.Restart()

            // Dynamic generation of nested loops based on dimension k
            let rec sampleMatrixElements (currentPoint: int list) (dimension: int) (accumulator: ResizeArray<int>) =
                if dimension = k then
                    // Convert the list to an array in reverse order (since we built it backwards)
                    let point = currentPoint |> List.rev |> Array.ofList
                    accumulator.Add((matrix.x_y point).getValues() |> Seq.length)
                else
                    // Continue building the point by trying all values in the current dimension
                    for i in 0..(sampleSize-1) do
                        sampleMatrixElements (i :: currentPoint) (dimension + 1) accumulator

            let nonZerosCounts = ResizeArray<int>()
            sampleMatrixElements [] 0 nonZerosCounts
            let matrixNonZerosSample = nonZerosCounts |> Seq.sum
            let samplingTime = stopwatch.ElapsedMilliseconds

            // Extrapolate to estimate full matrix non-zeros
            let estimatedMatrixNonZeros =
                if sampleSize = d then
                    matrixNonZerosSample
                else
                    let sampleVolume = pown (int64 sampleSize) k |> int
                    let scaleFactor = (float matrixSize) / (float sampleVolume)
                    int (float matrixNonZerosSample * scaleFactor)

            let vectorNonZeros = vector.getValues() |> Seq.length

            // Perform multiplication
            output.WriteLine("Starting matrix-vector multiplication...")
            stopwatch.Restart()
            resetMatrixTimingCounters()
            let result = matrix * vector
            let multiplicationTime = stopwatch.ElapsedMilliseconds

            // Count result non-zeros
            let resultNonZeros = result.getValues() |> Seq.length

            // Report memory usage
            let currentProcess = Process.GetCurrentProcess()
            let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)

            // Output performance metrics
            output.WriteLine($"{k}D Performance Test (d={d}):")
            let dimensionsStr = String.Join("x", [|for _ in 1..k -> d.ToString()|])
            output.WriteLine($"  Matrix dimensions: {dimensionsStr}")
            output.WriteLine($"  Matrix size: {matrixSize} x {matrixSize}")
            output.WriteLine($"  Matrix sampling time: {samplingTime} ms")
            output.WriteLine($"  Matrix non-zeros (estimated): {estimatedMatrixNonZeros}")
            output.WriteLine($"  Matrix sparsity: {(float estimatedMatrixNonZeros * 100.0 / float (matrixSize * matrixSize)):E6}%%")
            output.WriteLine($"  Vector non-zeros: {vectorNonZeros}")
            output.WriteLine($"  Vector sparsity: {(float vectorNonZeros * 100.0 / float matrixSize):F6}%%")
            output.WriteLine($"  Result non-zeros: {resultNonZeros}")
            output.WriteLine($"  Matrix functions creation time: {creationTime} ms")
            output.WriteLine($"  Vector creation time: {vectorCreationTime} ms")
            output.WriteLine($"  Multiplication time: {multiplicationTime} ms")
            output.WriteLine($"  Total test time: {creationTime + vectorCreationTime + samplingTime + multiplicationTime} ms")
            output.WriteLine($"  Approximate memory usage: {memoryMB} MB")

            let xyTotalTimeMs = float (xyTotalTime * 1000L) / (float Stopwatch.Frequency)
            output.WriteLine($"  Matrix x_y/y_x calls: {xyCallCount} calls taking {xyTotalTimeMs} ms total ({((float xyTotalTimeMs) / (float xyCallCount)):F6} ms avg)")

            // Return the results for potential further analysis
            (multiplicationTime, memoryMB)

        [<Fact>]
        let ``2D Tridiagonal Matrix Performance Test``() =
            runPerformanceTest 1000 2 |> ignore

        [<Fact>]
        let ``3D Tridiagonal Matrix Performance Test``() =
            runPerformanceTest 500 3 |> ignore

        [<Fact>]
        let ``4D Tridiagonal Matrix Performance Test``() =
            runPerformanceTest 100 4 |> ignore

        [<Fact>]
        let ``5D Tridiagonal Matrix Performance Test``() =
            runPerformanceTest 50 5 |> ignore

        [<Fact>]
        let ``6D Tridiagonal Matrix Performance Test``() =
            runPerformanceTest 25 6 |> ignore
