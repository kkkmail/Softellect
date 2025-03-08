namespace Softellect.Tests.MathTests

open System
open System.Diagnostics
open Xunit
open Xunit.Abstractions
open Softellect.Math.Sparse

module MultidimensionalTests4D =
    /// 4D point representation
    type Point4D = { x: int; y: int; z: int; w: int }

    /// Helper functions for creating 4D test data
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

        /// Create a 3-diagonal sparse matrix for 4D space
        let create4DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point4D, float> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith "Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the matrix as functions (no full instantiation)
            {
                x_y = fun point ->
                    let i, j, k, l = point.x, point.y, point.z, point.w
                    let values = ResizeArray<SparseValue<Point4D, float>>()

                    // Only include elements that would be non-zero

                    // Diagonal element
                    values.Add({ x = point; value = a })

                    // Off-diagonal in x direction
                    if i > 0 then
                        values.Add({ x = { x = i - 1; y = j; z = k; w = l }; value = b })

                    if i < d - 1 then
                        values.Add({ x = { x = i + 1; y = j; z = k; w = l }; value = b })

                    // Off-diagonal in y direction
                    if j > 0 then
                        values.Add({ x = { x = i; y = j - 1; z = k; w = l }; value = b })

                    if j < d - 1 then
                        values.Add({ x = { x = i; y = j + 1; z = k; w = l }; value = b })

                    // Off-diagonal in z direction
                    if k > 0 then
                        values.Add({ x = { x = i; y = j; z = k - 1; w = l }; value = b })

                    if k < d - 1 then
                        values.Add({ x = { x = i; y = j; z = k + 1; w = l }; value = b })

                    // Off-diagonal in w direction
                    if l > 0 then
                        values.Add({ x = { x = i; y = j; z = k; w = l - 1 }; value = b })

                    if l < d - 1 then
                        values.Add({ x = { x = i; y = j; z = k; w = l + 1 }; value = b })

                    SparseArray.create (values.ToArray())

                y_x = fun point ->
                    let i, j, k, l = point.x, point.y, point.z, point.w
                    let values = ResizeArray<SparseValue<Point4D, float>>()

                    // Only include elements that would be non-zero

                    // Diagonal element
                    values.Add({ x = point; value = a })

                    // Off-diagonal in x direction
                    if i > 0 then
                        values.Add({ x = { x = i - 1; y = j; z = k; w = l }; value = b })

                    if i < d - 1 then
                        values.Add({ x = { x = i + 1; y = j; z = k; w = l }; value = b })

                    // Off-diagonal in y direction
                    if j > 0 then
                        values.Add({ x = { x = i; y = j - 1; z = k; w = l }; value = b })

                    if j < d - 1 then
                        values.Add({ x = { x = i; y = j + 1; z = k; w = l }; value = b })

                    // Off-diagonal in z direction
                    if k > 0 then
                        values.Add({ x = { x = i; y = j; z = k - 1; w = l }; value = b })

                    if k < d - 1 then
                        values.Add({ x = { x = i; y = j; z = k + 1; w = l }; value = b })

                    // Off-diagonal in w direction
                    if l > 0 then
                        values.Add({ x = { x = i; y = j; z = k; w = l - 1 }; value = b })

                    if l < d - 1 then
                        values.Add({ x = { x = i; y = j; z = k; w = l + 1 }; value = b })

                    SparseArray.create (values.ToArray())
            }

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
                                values.Add({ x = { x = i; y = j; z = k; w = l }; value = 1.0 })

            SparseArray.create (values.ToArray())

    /// Performance tests for 4D sparse matrix operations
    [<Trait("Category", "Performance")>]
    type PerformanceTests4D(output: ITestOutputHelper) =

        [<Fact>]
        let ``4D Tridiagonal Matrix Performance Test``() =
            // Default parameters optimized for d = 100
            let d = 200  // d points per dimension (d^4 total points)
            let a = 0.5  // Diagonal element value
            let radius = 0.7 // Hypersphere radius
            let epsilon = 0.05 // Thickness of the hypersphere

            output.WriteLine($"Starting 4D performance test with d={d}...")

            // Create the matrix (just defines the functions, doesn't instantiate all values)
            let stopwatch = Stopwatch.StartNew()
            let matrix = Helpers.create4DTridiagonalMatrix d a
            let creationTime = stopwatch.ElapsedMilliseconds
            output.WriteLine($"Matrix functions created in {creationTime} ms")

            // Create the vector (3-sphere in 4D case)
            output.WriteLine("Creating hypersphere vector...")
            stopwatch.Restart()
            let vector = Helpers.create4DHypersphereVector d radius epsilon
            let vectorCreationTime = stopwatch.ElapsedMilliseconds

            // Count a sample of non-zero elements (not the full matrix)
            let matrixSize = d * d * d * d |> int64 // d^4
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
                                    let count = (matrix.x_y { x = i; y = j; z = k; w = l }).getValues() |> Seq.length
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
            output.WriteLine($"  Matrix non-zeros (estimated): {estimatedMatrixNonZeros} ({(float estimatedMatrixNonZeros * 100.0 / float (matrixSize * matrixSize)):F6})")
            output.WriteLine($"  Vector non-zeros: {vectorNonZeros} ({(float vectorNonZeros * 100.0 / float matrixSize):F6})")
            output.WriteLine($"  Result non-zeros: {resultNonZeros}")
            output.WriteLine($"  Matrix functions creation time: {creationTime} ms")
            output.WriteLine($"  Vector creation time: {vectorCreationTime} ms")
            output.WriteLine($"  Multiplication time: {multiplicationTime} ms")
            output.WriteLine($"  Total test time: {creationTime + vectorCreationTime + samplingTime + multiplicationTime} ms")

            // Report memory usage if possible
            let currentProcess = Process.GetCurrentProcess()
            let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)
            output.WriteLine($"  Approximate memory usage: {memoryMB} MB")
