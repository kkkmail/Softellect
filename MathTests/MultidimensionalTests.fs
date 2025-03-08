namespace Softellect.Tests.MathTests

open System
open System.Diagnostics
open Xunit
open FluentAssertions
open Softellect.Math.Sparse

/// Module for testing multidimensional sparse matrices
module MultidimensionalTests =
    /// 2D point representation
    type Point2D = { x: int; y: int }

    /// 3D point representation
    type Point3D = { x: int; y: int; z: int }

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

        /// Create a 3-diagonal sparse matrix for 2D space
        let create2DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point2D, float> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith "Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the matrix as functions (no full instantiation)
            {
                x_y = fun point ->
                    let i, j = point.x, point.y
                    let values = ResizeArray<SparseValue<Point2D, float>>()

                    // Only include elements that would be non-zero
                    // Diagonal element
                    values.Add({ x = point; value = a })

                    // Off-diagonal in x direction
                    if i > 0 then
                        values.Add({ x = { x = i - 1; y = j }; value = b })

                    if i < d - 1 then
                        values.Add({ x = { x = i + 1; y = j }; value = b })

                    // Off-diagonal in y direction
                    if j > 0 then
                        values.Add({ x = { x = i; y = j - 1 }; value = b })

                    if j < d - 1 then
                        values.Add({ x = { x = i; y = j + 1 }; value = b })

                    SparseArray.create (values.ToArray())

                y_x = fun point ->
                    let i, j = point.x, point.y
                    let values = ResizeArray<SparseValue<Point2D, float>>()

                    // Only include elements that would be non-zero
                    // Diagonal element
                    values.Add({ x = point; value = a })

                    // Off-diagonal in x direction
                    if i > 0 then
                        values.Add({ x = { x = i - 1; y = j }; value = b })

                    if i < d - 1 then
                        values.Add({ x = { x = i + 1; y = j }; value = b })

                    // Off-diagonal in y direction
                    if j > 0 then
                        values.Add({ x = { x = i; y = j - 1 }; value = b })

                    if j < d - 1 then
                        values.Add({ x = { x = i; y = j + 1 }; value = b })

                    SparseArray.create (values.ToArray())
            }

        /// Create a 3-diagonal sparse matrix for 3D space
        let create3DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point3D, float> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith "Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the matrix as functions (no full instantiation)
            {
                x_y = fun point ->
                    let i, j, k = point.x, point.y, point.z
                    let values = ResizeArray<SparseValue<Point3D, float>>()

                    // Only include elements that would be non-zero
                    // Diagonal element
                    values.Add({ x = point; value = a })

                    // Off-diagonal in x direction
                    if i > 0 then
                        values.Add({ x = { x = i - 1; y = j; z = k }; value = b })

                    if i < d - 1 then
                        values.Add({ x = { x = i + 1; y = j; z = k }; value = b })

                    // Off-diagonal in y direction
                    if j > 0 then
                        values.Add({ x = { x = i; y = j - 1; z = k }; value = b })

                    if j < d - 1 then
                        values.Add({ x = { x = i; y = j + 1; z = k }; value = b })

                    // Off-diagonal in z direction
                    if k > 0 then
                        values.Add({ x = { x = i; y = j; z = k - 1 }; value = b })

                    if k < d - 1 then
                        values.Add({ x = { x = i; y = j; z = k + 1 }; value = b })

                    SparseArray.create (values.ToArray())

                y_x = fun point ->
                    let i, j, k = point.x, point.y, point.z
                    let values = ResizeArray<SparseValue<Point3D, float>>()

                    // Only include elements that would be non-zero
                    // Diagonal element
                    values.Add({ x = point; value = a })

                    // Off-diagonal in x direction
                    if i > 0 then
                        values.Add({ x = { x = i - 1; y = j; z = k }; value = b })

                    if i < d - 1 then
                        values.Add({ x = { x = i + 1; y = j; z = k }; value = b })

                    // Off-diagonal in y direction
                    if j > 0 then
                        values.Add({ x = { x = i; y = j - 1; z = k }; value = b })

                    if j < d - 1 then
                        values.Add({ x = { x = i; y = j + 1; z = k }; value = b })

                    // Off-diagonal in z direction
                    if k > 0 then
                        values.Add({ x = { x = i; y = j; z = k - 1 }; value = b })

                    if k < d - 1 then
                        values.Add({ x = { x = i; y = j; z = k + 1 }; value = b })

                    SparseArray.create (values.ToArray())
            }

        /// Create a thin hypersphere vector in 2D
        let create2DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point2D, float> =
            let values = ResizeArray<SparseValue<Point2D, float>>()

            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    let x = normalizeIndex d i
                    let y = normalizeIndex d j

                    // Check if point is on the hypersphere (circle in 2D)
                    if isNearHypersphere radius epsilon [|x; y|] then
                        values.Add({ x = { x = i; y = j }; value = 1.0 })

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
                            values.Add({ x = { x = i; y = j; z = k }; value = 1.0 })

            SparseArray.create (values.ToArray())

    open Helpers

    /// Performance tests for multidimensional sparse matrix operations
    [<Trait("Category", "Performance")>]
    type PerformanceTests(output: Xunit.Abstractions.ITestOutputHelper) =

        let d = 500 // d points per dimension

        [<Fact>]
        let ``2D Tridiagonal Matrix Performance Test``() =
            // Parameters (can be adjusted for testing)
            let a = 0.5  // Diagonal element value
            let radius = 0.7 // Hypersphere radius
            let epsilon = 0.05 // Thickness of the hypersphere

            // Create the matrix (just defines the functions, doesn't instantiate all values)
            let stopwatch = Stopwatch.StartNew()
            let matrix = create2DTridiagonalMatrix d a
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
                            let count = (matrix.x_y { x = i; y = j }).getValues() |> Seq.length
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
            output.WriteLine($"  Matrix non-zeros (estimated): {estimatedMatrixNonZeros} ({(float estimatedMatrixNonZeros * 100.0 / float (matrixSize * matrixSize)):F2})")
            output.WriteLine($"  Vector non-zeros: {vectorNonZeros} ({(float vectorNonZeros * 100.0 / float matrixSize):F2})")
            output.WriteLine($"  Result non-zeros: {resultNonZeros}")
            output.WriteLine($"  Matrix creation time: {creationTime} ms")
            output.WriteLine($"  Vector creation time: {vectorCreationTime} ms")
            output.WriteLine($"  Multiplication time: {multiplicationTime} ms")

        [<Fact>]
        let ``3D Tridiagonal Matrix Performance Test``() =
            let a = 0.5  // Diagonal element value
            let radius = 0.7 // Hypersphere radius
            let epsilon = 0.05 // Thickness of the hypersphere

            // Create the matrix (just defines the functions, doesn't instantiate all values)
            let stopwatch = Stopwatch.StartNew()
            let matrix = create3DTridiagonalMatrix d a
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
                                let count = (matrix.x_y { x = i; y = j; z = k }).getValues() |> Seq.length
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
            output.WriteLine($"  Matrix non-zeros (estimated): {estimatedMatrixNonZeros} ({(float estimatedMatrixNonZeros * 100.0 / float (matrixSize * matrixSize)):F2})")
            output.WriteLine($"  Vector non-zeros: {vectorNonZeros} ({(float vectorNonZeros * 100.0 / float matrixSize):F2})")
            output.WriteLine($"  Result non-zeros: {resultNonZeros}")
            output.WriteLine($"  Matrix creation time: {creationTime} ms")
            output.WriteLine($"  Vector creation time: {vectorCreationTime} ms")
            output.WriteLine($"  Multiplication time: {multiplicationTime} ms")
