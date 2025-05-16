namespace Softellect.Tests.MathTests

open System
open System.Collections.Generic
open System.Diagnostics
open Xunit
open FluentAssertions
open Softellect.Math.Sparse
open Softellect.Math.Primitives
open Softellect.Math.Tridiagonal

/// Module for testing multidimensional sparse matrices
/// Current results:
///
///     2D Performance Test (d=500):
///   Matrix size: 250000 x 250000 (theoretical: 62500000000 elements)
///   Matrix non-zeros (estimated): 1245000 (0.00)
///   Vector non-zeros: 27364 (10.95)
///   Result non-zeros: 29340
///   Matrix creation time: 0 ms
///   Vector creation time: 19 ms
///   Multiplication time: 51 ms
///
///     3D Performance Test (d=500):
///   Matrix size: 125000000 x 125000000 (theoretical: 15625000000000000 elements)
///   Matrix non-zeros (estimated): 837500000 (0.00)
///   Vector non-zeros: 9580624 (7.66)
///   Result non-zeros: 10221264
///   Matrix creation time: 0 ms
///   Vector creation time: 8138 ms
///   Multiplication time: 21130 ms
///
///     2D Performance Test (d=1000):
///   Matrix size: 1000000 x 1000000 (theoretical: 1000000000000 elements)
///   Matrix non-zeros (estimated): 4980000 (0.00)
///   Vector non-zeros: 109724 (10.97)
///   Result non-zeros: 113680
///   Matrix creation time: 0 ms
///   Vector creation time: 60 ms
///   Multiplication time: 203 ms
///
///     3D Performance Test (d=1000):
///   Matrix size: 1000000000 x 1000000000 (theoretical: 1000000000000000000 elements)
///   Matrix non-zeros (estimated): 2147483647 (0.00)
///   Vector non-zeros: 76868488 (7.69)
///   Result non-zeros: 79435872
///   Matrix creation time: 0 ms
///   Vector creation time: 67445 ms
///   Multiplication time: 287480 ms
///
///     Starting 4D performance test with d=100...
/// Matrix functions created in 0 ms
/// Creating hypersphere vector...
/// Total theoretical matrix size: 100000000 x 100000000
/// Sampling 5^4 elements to estimate matrix density...
/// Starting matrix-vector multiplication...
/// 4D Performance Test (d=100):
///   Matrix dimensions: 100x100x100x100
///   Matrix size: 100000000 x 100000000 (theoretical: 10000000000000000 elements)
///   Matrix sampling time: 4 ms
///   Matrix non-zeros (estimated): 820000000 (43.735213)
///   Vector non-zeros: 4075408 (4.075408)
///   Result non-zeros: 5381104
///   Matrix functions creation time: 0 ms
///   Vector creation time: 6295 ms
///   Multiplication time: 10919 ms
///   Total test time: 17218 ms
///   Approximate memory usage: 1090 MB
///
///     Starting 4D performance test with d=200...
/// Matrix functions created in 0 ms
/// Creating hypersphere vector...
/// Total theoretical matrix size: 1600000000 x 1600000000
/// Sampling 5^4 elements to estimate matrix density...
/// Starting matrix-vector multiplication...
/// 4D Performance Test (d=200):
///   Matrix dimensions: 200x200x200x200
///   Matrix size: 1600000000 x 1600000000 (theoretical: 2560000000000000000 elements)
///   Matrix sampling time: 4 ms
///   Matrix non-zeros (estimated): 2147483647 (-203.174603)
///   Vector non-zeros: 66703376 (4.168961)
///   Result non-zeros: 77274368
///   Matrix functions creation time: 0 ms
///   Vector creation time: 103462 ms
///   Multiplication time: 226104 ms
///   Total test time: 329570 ms
///   Approximate memory usage: 16202 MB
///
module MultidimensionalTests =

    /// Helper functions for creating multi-dimensional test data
    module Helpers =
        let mutable xyCallCount = 0
        let mutable xyTotalTime = 0L
        let xyStopwatch = Stopwatch()

        /// Reset the timing counters (call this before each test)
        let resetMatrixTimingCounters() =
            xyCallCount <- 0
            xyTotalTime <- 0L


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

        /// Create a thin hypersphere vector in 2D
        let create2DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point2D, float> =
            let values = ResizeArray<SparseValue<Point2D, float>>()

            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    let x = normalizeIndex d i
                    let y = normalizeIndex d j

                    // Check if point is on the hypersphere (circle in 2D)
                    if isNearHypersphere radius epsilon [|x; y|] then
                        values.Add({ x = { i0 = i; i1 = j }; value = 1.0 })

            SparseArray.create (values.ToArray())

        /// Create a thin hypersphere vector in 3D
        let create3DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point3D, float> =
            let values = ResizeArray<SparseValue<Point3D, float>>()

            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    for k in 0..(d-1) do
                        let x = normalizeIndex d i
                        let y = normalizeIndex d j
                        let z = normalizeIndex d k

                        // Check if point is on the hypersphere (sphere in 3D)
                        if isNearHypersphere radius epsilon [|x; y; z|] then
                            values.Add({ x = { i0 = i; i1 = j; i2 = k }; value = 1.0 })

            SparseArray.create (values.ToArray())


        /// Create a thin hypersphere vector in 4D (a 3-sphere)
        let create4DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point4D, float> =
            let values = ResizeArray<SparseValue<Point4D, float>>()

            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    for k in 0..(d-1) do
                        for l in 0..(d-1) do
                            let x = normalizeIndex d i
                            let y = normalizeIndex d j
                            let z = normalizeIndex d k
                            let w = normalizeIndex d l

                            // Check if point is on the hypersphere (3-sphere in 4D)
                            if isNearHypersphere radius epsilon [|x; y; z; w|] then
                                values.Add({ x = { i0 = i; i1 = j; i2 = k; i3 = l }; value = 1.0 })

            SparseArray.create (values.ToArray())


        /// Create a fully instantiated sparse matrix for 2D space using traditional approach
        let createTraditional2DTridiagonalMatrix (d: int) (a: float) : Dictionary<Point2D, Dictionary<Point2D, float>> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith "Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the full matrix representation
            let matrix = Dictionary<Point2D, Dictionary<Point2D, float>>()

            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    let fromPoint = { i0 = i; i1 = j }
                    let toPoints = Dictionary<Point2D, float>()

                    // Diagonal element
                    toPoints.Add(fromPoint, a)

                    // Off-diagonal in x direction
                    if i > 0 then
                        toPoints.Add({ i0 = i - 1; i1 = j }, b)

                    if i < d - 1 then
                        toPoints.Add({ i0 = i + 1; i1 = j }, b)

                    // Off-diagonal in y direction
                    if j > 0 then
                        toPoints.Add({ i0 = i; i1 = j - 1 }, b)

                    if j < d - 1 then
                        toPoints.Add({ i0 = i; i1 = j + 1 }, b)

                    matrix.Add(fromPoint, toPoints)

            matrix

        /// Multiply matrix by vector using traditional full scan approach by x but sparse access by y
        let multiplyTraditionalWithSparseVector (matrix: Dictionary<Point2D, Dictionary<Point2D, float>>)
                                              (vector: SparseArray<Point2D, float>) : Dictionary<Point2D, float> =
            let result = Dictionary<Point2D, float>()

            // For each row in the matrix - full scan by x
            for KeyValue(fromPoint, toPointsDict) in matrix do
                let mutable rowResult = 0.0

                // For each non-zero element in vector - sparse scan by y
                for sparseValue in vector.values do
                    let vectorPoint = sparseValue.x
                    let vectorValue = sparseValue.value

                    // If this vector point has a connection in this matrix row
                    if toPointsDict.ContainsKey(vectorPoint) then
                        rowResult <- rowResult + toPointsDict.[vectorPoint] * vectorValue

                // Only add non-zero results
                if abs rowResult > 1e-10 then
                    result.Add(fromPoint, rowResult)

            result


    open Helpers


    /// Performance tests for multidimensional sparse matrix operations
    [<Trait("Category", "Performance")>]
    type PerformanceTests(output: Xunit.Abstractions.ITestOutputHelper) =

        let d = 500 // d points per dimension for 2D and 3D tests.
        let d4 = 100 // d points per dimension (d^4 total points)

        [<Fact>]
        let ``2D Tridiagonal Matrix Performance Test``() =
            // Parameters (can be adjusted for testing)
            let a = 0.5  // Diagonal element value
            let radius = 0.7 // Hypersphere radius
            let epsilon = 0.05 // Thickness of the hypersphere

            // Create the matrix (just defines the functions, doesn't instantiate all values)
            let stopwatch = Stopwatch.StartNew()
            let matrix = createTridiagonalMatrix2D d a
            let creationTime = stopwatch.ElapsedMilliseconds

            // Create the vector (circle in 2D case)
            stopwatch.Restart()
            let vector = create2DHypersphereVector d radius epsilon
            let vectorCreationTime = stopwatch.ElapsedMilliseconds

            // Count a sample of non-zero elements (not the full matrix)
            let matrixSize = d * d |> int64
            let sampleSize = min 100 d // Take a reasonably sized sample
            let matrixNonZerosSample =
                seq {
                    for i in 0..(sampleSize-1) do
                        for j in 0..(sampleSize-1) do
                            let count = (matrix.x_y { i0 = i; i1 = j }).getValues() |> Seq.length
                            yield count
                }
                |> Seq.sum

            // Extrapolate to estimate full matrix non-zeros
            let estimatedMatrixNonZeros =
                if sampleSize = d then
                    matrixNonZerosSample
                else
                    let scaleFactor = (float matrixSize) / (float (sampleSize * sampleSize))
                    int (float matrixNonZerosSample * scaleFactor)

            let vectorNonZeros = vector.getValues() |> Seq.length

            // Perform multiplication
            stopwatch.Restart()
            let result = matrix * vector
            let multiplicationTime = stopwatch.ElapsedMilliseconds

            // Count result non-zeros
            let resultNonZeros = result.getValues() |> Seq.length

            // Output performance metrics using ITestOutputHelper
            output.WriteLine($"2D Performance Test (d={d}):")
            output.WriteLine($"  Matrix size: {matrixSize} x {matrixSize} (theoretical: {matrixSize * matrixSize} elements)")
            output.WriteLine($"  Matrix non-zeros (estimated): {estimatedMatrixNonZeros} ({(float estimatedMatrixNonZeros * 100.0 / float (matrixSize * matrixSize)):E4}%%)")
            output.WriteLine($"  Vector non-zeros: {vectorNonZeros} ({(float vectorNonZeros * 100.0 / float matrixSize):F4}%%)")
            output.WriteLine($"  Result non-zeros: {resultNonZeros}")
            output.WriteLine($"  Matrix creation time: {creationTime} ms")
            output.WriteLine($"  Vector creation time: {vectorCreationTime} ms")
            output.WriteLine($"  Multiplication time: {multiplicationTime} ms")

            // Report memory usage if possible
            let currentProcess = Process.GetCurrentProcess()
            let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)
            output.WriteLine($"  Approximate memory usage: {memoryMB} MB")

            let xyTotalTimeMs = float (Helpers.xyTotalTime * 1000L) / (float Stopwatch.Frequency)
            output.WriteLine($"  Matrix x_y/y_x calls: {Helpers.xyCallCount} calls taking {xyTotalTimeMs} ms total ({((float xyTotalTimeMs) / (float Helpers.xyCallCount)):F6} ms avg)")


        [<Fact>]
        let ``3D Tridiagonal Matrix Performance Test``() =
            let a = 0.5  // Diagonal element value
            let radius = 0.7 // Hypersphere radius
            let epsilon = 0.05 // Thickness of the hypersphere

            // Create the matrix (just defines the functions, doesn't instantiate all values)
            let stopwatch = Stopwatch.StartNew()
            let matrix = createTridiagonalMatrix3D d a
            let creationTime = stopwatch.ElapsedMilliseconds

            // Create the vector (sphere in 3D case)
            stopwatch.Restart()
            let vector = create3DHypersphereVector d radius epsilon
            let vectorCreationTime = stopwatch.ElapsedMilliseconds

            // Count a sample of non-zero elements (not the full matrix)
            let matrixSize = d * d * d |> int64
            let sampleSize = min 10 d // Take a reasonably sized sample
            let matrixNonZerosSample =
                seq {
                    for i in 0..(sampleSize-1) do
                        for j in 0..(sampleSize-1) do
                            for k in 0..(sampleSize-1) do
                                let count = (matrix.x_y { i0 = i; i1 = j; i2 = k }).getValues() |> Seq.length
                                yield count
                }
                |> Seq.sum

            // Extrapolate to estimate full matrix non-zeros
            let estimatedMatrixNonZeros =
                if sampleSize = d then
                    matrixNonZerosSample
                else
                    let scaleFactor = (float matrixSize) / (float (sampleSize * sampleSize * sampleSize))
                    int (float matrixNonZerosSample * scaleFactor)

            let vectorNonZeros = vector.getValues() |> Seq.length

            // Perform multiplication
            stopwatch.Restart()
            let result = matrix * vector
            let multiplicationTime = stopwatch.ElapsedMilliseconds

            // Count result non-zeros
            let resultNonZeros = result.getValues() |> Seq.length

            // Output performance metrics using ITestOutputHelper
            output.WriteLine($"3D Performance Test (d={d}):")
            output.WriteLine($"  Matrix size: {matrixSize} x {matrixSize} (theoretical: {matrixSize * matrixSize} elements)")
            output.WriteLine($"  Matrix non-zeros (estimated): {estimatedMatrixNonZeros} ({(float estimatedMatrixNonZeros * 100.0 / float (matrixSize * matrixSize)):E4}%%)")
            output.WriteLine($"  Vector non-zeros: {vectorNonZeros} ({(float vectorNonZeros * 100.0 / float matrixSize):F4}%%)")
            output.WriteLine($"  Result non-zeros: {resultNonZeros}")
            output.WriteLine($"  Matrix creation time: {creationTime} ms")
            output.WriteLine($"  Vector creation time: {vectorCreationTime} ms")
            output.WriteLine($"  Multiplication time: {multiplicationTime} ms")

            // Report memory usage if possible
            let currentProcess = Process.GetCurrentProcess()
            let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)
            output.WriteLine($"  Approximate memory usage: {memoryMB} MB")

            let xyTotalTimeMs = float (Helpers.xyTotalTime * 1000L) / (float Stopwatch.Frequency)
            output.WriteLine($"  Matrix x_y/y_x calls: {Helpers.xyCallCount} calls taking {xyTotalTimeMs} ms total ({((float xyTotalTimeMs) / (float Helpers.xyCallCount)):F6} ms avg)")


        [<Fact>]
        let ``4D Tridiagonal Matrix Performance Test``() =
            // Default parameters optimized for d = 100
            let d = d4  // d points per dimension (d^4 total points)
            let a = 0.5  // Diagonal element value
            let radius = 0.7 // Hypersphere radius
            let epsilon = 0.05 // Thickness of the hypersphere

            output.WriteLine($"Starting 4D performance test with d={d}...")

            // Create the matrix (just defines the functions, doesn't instantiate all values)
            let stopwatch = Stopwatch.StartNew()
            let matrix = createTridiagonalMatrix4D d a
            let creationTime = stopwatch.ElapsedMilliseconds
            output.WriteLine($"Matrix functions created in {creationTime} ms")

            // Create the vector (3-sphere in 4D case)
            output.WriteLine("Creating hypersphere vector...")
            stopwatch.Restart()
            let vector = Helpers.create4DHypersphereVector d radius epsilon
            let vectorCreationTime = stopwatch.ElapsedMilliseconds

            // Count a sample of non-zero elements (not the full matrix)
            let matrixSize = pown (int64 d) 4 // d^4
            output.WriteLine($"Total theoretical matrix size: {matrixSize} x {matrixSize}")

            // Use a smaller sample size due to 4D complexity
            let sampleSize = min 5 d // Take a small sample
            output.WriteLine($"Sampling {sampleSize}^4 elements to estimate matrix density...")

            stopwatch.Restart()
            let matrixNonZerosSample =
                seq {
                    for i in 0..(sampleSize-1) do
                        for j in 0..(sampleSize-1) do
                            for k in 0..(sampleSize-1) do
                                for l in 0..(sampleSize-1) do
                                    let count = (matrix.x_y { i0 = i; i1 = j; i2 = k; i3 = l }).getValues() |> Seq.length
                                    yield count
                }
                |> Seq.sum
            let samplingTime = stopwatch.ElapsedMilliseconds

            // Extrapolate to estimate full matrix non-zeros
            let estimatedMatrixNonZeros =
                if sampleSize = d then
                    matrixNonZerosSample
                else
                    let scaleFactor = (float matrixSize) / (float (sampleSize * sampleSize * sampleSize * sampleSize))
                    int (float matrixNonZerosSample * scaleFactor)

            let vectorNonZeros = vector.getValues() |> Seq.length

            // Perform multiplication
            output.WriteLine("Starting matrix-vector multiplication...")
            stopwatch.Restart()
            let result = matrix * vector
            let multiplicationTime = stopwatch.ElapsedMilliseconds

            // Count result non-zeros
            let resultNonZeros = result.getValues() |> Seq.length

            // Output performance metrics
            output.WriteLine($"4D Performance Test (d={d}):")
            output.WriteLine($"  Matrix dimensions: {d}x{d}x{d}x{d}")
            output.WriteLine($"  Matrix size: {matrixSize} x {matrixSize} (theoretical: {Int64.Parse(matrixSize.ToString()) * Int64.Parse(matrixSize.ToString())} elements)")
            output.WriteLine($"  Matrix sampling time: {samplingTime} ms")
            output.WriteLine($"  Matrix non-zeros (estimated): {estimatedMatrixNonZeros} ({(float estimatedMatrixNonZeros * 100.0 / float (matrixSize * matrixSize)):E4}%%)")
            output.WriteLine($"  Vector non-zeros: {vectorNonZeros} ({(float vectorNonZeros * 100.0 / float matrixSize):F4}%%)")
            output.WriteLine($"  Result non-zeros: {resultNonZeros}")
            output.WriteLine($"  Matrix functions creation time: {creationTime} ms")
            output.WriteLine($"  Vector creation time: {vectorCreationTime} ms")
            output.WriteLine($"  Multiplication time: {multiplicationTime} ms")
            output.WriteLine($"  Total test time: {creationTime + vectorCreationTime + samplingTime + multiplicationTime} ms")

            // Report memory usage if possible
            let currentProcess = Process.GetCurrentProcess()
            let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)
            output.WriteLine($"  Approximate memory usage: {memoryMB} MB")

            let xyTotalTimeMs = float (Helpers.xyTotalTime * 1000L) / (float Stopwatch.Frequency)
            output.WriteLine($"  Matrix x_y/y_x calls: {Helpers.xyCallCount} calls taking {xyTotalTimeMs} ms total ({((float xyTotalTimeMs) / (float Helpers.xyCallCount)):F6} ms avg)")


        /// Performance test for traditional matrix approach
        [<Fact(Skip = "Too slow.")>]
        let ``2D Traditional Matrix Multiplication Performance Test``() =
            // Parameters
            let a = 0.5   // Diagonal element value
            let radius = 0.7 // Hypersphere radius
            let epsilon = 0.05 // Thickness of the hypersphere

            output.WriteLine($"Starting 2D traditional matrix test with d={d}")

            // Create the matrix with full instantiation
            let stopwatch = Stopwatch.StartNew()
            let matrix = createTraditional2DTridiagonalMatrix d a
            let creationTime = stopwatch.ElapsedMilliseconds

            // Create the vector (circle in 2D case)
            stopwatch.Restart()
            let vector = create2DHypersphereVector d radius epsilon
            let vectorCreationTime = stopwatch.ElapsedMilliseconds

            // Count non-zero elements
            let matrixSize = d * d |> int64
            let matrixNonZeros =
                matrix.Values
                |> Seq.sumBy (fun dict -> dict.Count)

            let vectorNonZeros = vector.getValues() |> Seq.length

            // Perform multiplication using the traditional full scan approach
            output.WriteLine("Starting traditional matrix-vector multiplication...")
            stopwatch.Restart()
            let result = multiplyTraditionalWithSparseVector matrix vector
            let multiplicationTime = stopwatch.ElapsedMilliseconds

            // Count result non-zeros
            let resultNonZeros = result.Count

            // Report memory usage
            let currentProcess = Process.GetCurrentProcess()
            let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)

            // Output performance metrics
            output.WriteLine($"2D Traditional Matrix Performance Test (d={d}):")
            output.WriteLine($"  Matrix size: {matrixSize} x {matrixSize} (theoretical: {matrixSize * matrixSize} elements)")
            output.WriteLine($"  Matrix non-zeros: {matrixNonZeros} ({(float matrixNonZeros * 100.0 / float (matrixSize * matrixSize)):E4}%%)")
            output.WriteLine($"  Vector non-zeros: {vectorNonZeros} ({(float vectorNonZeros * 100.0 / float matrixSize):F4}%%)")
            output.WriteLine($"  Result non-zeros: {resultNonZeros}")
            output.WriteLine($"  Matrix creation time: {creationTime} ms")
            output.WriteLine($"  Vector creation time: {vectorCreationTime} ms")
            output.WriteLine($"  Multiplication time: {multiplicationTime} ms")
            output.WriteLine($"  Total test time: {creationTime + vectorCreationTime + multiplicationTime} ms")
            output.WriteLine($"  Approximate memory usage: {memoryMB} MB")
