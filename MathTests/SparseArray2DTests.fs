namespace Softellect.Tests.MathTests

open FluentAssertions.Execution
open Softellect.Math.FredholmKernel
open Xunit
open FluentAssertions
open Softellect.Math.Primitives

/// Helper functions for testing
module TestHelpers =
    /// Convert a Matrix<'T> to a 2D array for comparison
    let matrixToArray2D (matrix: Matrix<int64>) : int64[,] =
        let rows = matrix.value.Length
        let cols = if rows > 0 then matrix.value[0].Length else 0
        let result = Array2D.zeroCreate rows cols
        for i in 0..rows-1 do
            for j in 0..cols-1 do
                result[i, j] <- matrix.value[i][j]
        result

    /// Convert a SparseArray2D<'T> to a 2D array for comparison
    let sparseToArray2D (sparse: SparseArray2D<int64>) (rows: int) (cols: int) : int64[,] =
        let result = Array2D.zeroCreate rows cols
        for value in sparse.value() do
            if value.i < rows && value.j < cols then
                result[value.i, value.j] <- value.value2D
        result

    /// Create a SparseArray2D from a 2D array
    let createSparseFrom2DArray (array: int64[,]) : SparseArray2D<int64> =
        let rows = array.GetLength(0)
        let cols = array.GetLength(1)
        let sparseValues =
            [|
                for i in 0..rows-1 do
                    for j in 0..cols-1 do
                        let value = array[i, j]
                        if value <> 0L then
                            yield { i = i; j = j; value2D = value }
            |]
        SparseArray2D.create sparseValues

    /// Create a Matrix from a 2D array
    let createMatrixFrom2DArray (array: int64[,]) : Matrix<int64> =
        let rows = array.GetLength(0)
        let cols = array.GetLength(1)
        let matrixArray =
            [|
                for i in 0..rows-1 do
                    [|
                        for j in 0..cols-1 do
                            array[i, j]
                    |]
            |]
        Matrix matrixArray

    /// Assert that a SparseArray2D and a Matrix have the same values
    let assertEqualSparseAndMatrix (sparse: SparseArray2D<int64>) (matrix: Matrix<int64>) =
        let rows = matrix.value.Length
        let cols = if rows > 0 then matrix.value[0].Length else 0
        let sparseArray = sparseToArray2D sparse rows cols
        let matrixArray = matrixToArray2D matrix

        for i in 0..rows-1 do
            for j in 0..cols-1 do
                sparseArray[i, j].Should().Be(matrixArray[i, j], $"at position [{i}, {j}]") |> ignore


/// Tests for SparseArray2D operators
type SparseArray2DOperatorTests() =

    // Test data
    let testData1 = array2D [
        [1L; 0L; 3L; 0L]
        [0L; 5L; 0L; 0L]
        [7L; 0L; 9L; 0L]
    ]

    let testData2 = array2D [
        [2L; 0L; 0L; 4L]
        [0L; 6L; 0L; 0L]
        [0L; 0L; 8L; 10L]
    ]

    [<Fact>]
    let ``Addition operator should combine values from both arrays`` () =
        // Arrange
        let sparse1 = TestHelpers.createSparseFrom2DArray testData1
        let sparse2 = TestHelpers.createSparseFrom2DArray testData2
        let matrix1 = TestHelpers.createMatrixFrom2DArray testData1
        let matrix2 = TestHelpers.createMatrixFrom2DArray testData2

        // Act
        let sparseResult = sparse1 + sparse2
        let matrixResult = matrix1 + matrix2

        // Assert
        TestHelpers.assertEqualSparseAndMatrix sparseResult matrixResult

    [<Fact>]
    let ``Subtraction operator should correctly subtract corresponding elements`` () =
        // Arrange
        let sparse1 = TestHelpers.createSparseFrom2DArray testData1
        let sparse2 = TestHelpers.createSparseFrom2DArray testData2
        let matrix1 = TestHelpers.createMatrixFrom2DArray testData1
        let matrix2 = TestHelpers.createMatrixFrom2DArray testData2

        // Act
        let sparseResult = sparse1 - sparse2
        let matrixResult = matrix1 - matrix2

        // Assert
        TestHelpers.assertEqualSparseAndMatrix sparseResult matrixResult

    [<Fact>]
    let ``Multiplication operator should multiply corresponding elements`` () =
        // Arrange
        let sparse1 = TestHelpers.createSparseFrom2DArray testData1
        let sparse2 = TestHelpers.createSparseFrom2DArray testData2
        let matrix1 = TestHelpers.createMatrixFrom2DArray testData1
        let matrix2 = TestHelpers.createMatrixFrom2DArray testData2

        // Act
        let sparseResult = sparse1 * sparse2
        let matrixResult = matrix1 * matrix2  // Assuming Matrix implements element-wise multiplication

        // Assert
        TestHelpers.assertEqualSparseAndMatrix sparseResult matrixResult

    [<Fact>]
    let ``Operations with zero elements should not affect results`` () =
        // Arrange
        let denseData = array2D [
            [1L; 2L; 3L]
            [4L; 5L; 6L]
            [7L; 8L; 9L]
        ]

        let sparseData = array2D [
            [0L; 2L; 0L]
            [4L; 0L; 6L]
            [0L; 8L; 0L]
        ]

        let denseSparse = TestHelpers.createSparseFrom2DArray denseData
        let sparseSparse = TestHelpers.createSparseFrom2DArray sparseData
        let denseMatrix = TestHelpers.createMatrixFrom2DArray denseData
        let sparseMatrix = TestHelpers.createMatrixFrom2DArray sparseData

        // Act
        let additionResult = denseSparse + sparseSparse
        let subtractionResult = denseSparse - sparseSparse
        let multiplicationResult = denseSparse * sparseSparse

        let matrixAddResult = denseMatrix + sparseMatrix
        let matrixSubResult = denseMatrix - sparseMatrix
        let matrixMulResult = denseMatrix * sparseMatrix

        // Assert
        TestHelpers.assertEqualSparseAndMatrix additionResult matrixAddResult
        TestHelpers.assertEqualSparseAndMatrix subtractionResult matrixSubResult
        TestHelpers.assertEqualSparseAndMatrix multiplicationResult matrixMulResult

    [<Fact>]
    let ``Empty sparse arrays should be handled correctly`` () =
        // Arrange
        let emptyData = array2D [
            [0L; 0L; 0L]
            [0L; 0L; 0L]
            [0L; 0L; 0L]
        ]

        let normalData = array2D [
            [1L; 2L; 3L]
            [4L; 5L; 6L]
            [7L; 8L; 9L]
        ]

        let emptySparse = TestHelpers.createSparseFrom2DArray emptyData
        let normalSparse = TestHelpers.createSparseFrom2DArray normalData
        let emptyMatrix = TestHelpers.createMatrixFrom2DArray emptyData
        let normalMatrix = TestHelpers.createMatrixFrom2DArray normalData

        // Act
        let additionResult = emptySparse + normalSparse
        let subtractionResult = normalSparse - emptySparse
        let multiplicationResult = emptySparse * normalSparse

        let matrixAddResult = emptyMatrix + normalMatrix
        let matrixSubResult = normalMatrix - emptyMatrix
        let matrixMulResult = emptyMatrix * normalMatrix

        // Assert
        TestHelpers.assertEqualSparseAndMatrix additionResult matrixAddResult
        TestHelpers.assertEqualSparseAndMatrix subtractionResult matrixSubResult
        TestHelpers.assertEqualSparseAndMatrix multiplicationResult matrixMulResult

        additionResult.Should().BeEquivalentTo(normalSparse) |> ignore

        // Multiplication with empty should be empty
        multiplicationResult.value().Should().BeEmpty()


    // =========================================================

    // Additional tests for the SparseArray2D type with separable functionality
    // Add these to your existing SparseArray2DOperatorTests class

    [<Fact>]
    let ``Separable arrays should be created and accessed correctly`` () =
        // Arrange
        let xValues = [|
            { i = 0; value1D = 2L }
            { i = 2; value1D = 3L }
        |]

        let yValues = [|
            { i = 1; value1D = 4L }
            { i = 3; value1D = 5L }
        |]

        // Act
        let separableSparseArray = SparseArray2D.create (xValues, yValues)

        // Assert
        // Test accessing some values
        (separableSparseArray.tryFind 0 1).Value.Should().Be(8L) |> ignore // 2 * 4
        (separableSparseArray.tryFind 0 3).Value.Should().Be(10L) |> ignore // 2 * 5
        (separableSparseArray.tryFind 2 1).Value.Should().Be(12L) |> ignore // 3 * 4
        (separableSparseArray.tryFind 2 3).Value.Should().Be(15L) |> ignore // 3 * 5

        // Test accessing non-existent values
        (separableSparseArray.tryFind 1 1).Should().BeNull() |> ignore
        (separableSparseArray.tryFind 0 0).Should().BeNull() |> ignore

    [<Fact>]
    let ``Separable array multiplication should remain separable`` () =
        // Arrange
        let xValues1 = [| { i = 0; value1D = 2L }; { i = 1; value1D = 3L } |]
        let yValues1 = [| { i = 0; value1D = 4L }; { i = 1; value1D = 5L } |]
        let separable1 = SparseArray2D.create (xValues1, yValues1)

        let xValues2 = [| { i = 0; value1D = 6L }; { i = 1; value1D = 7L } |]
        let yValues2 = [| { i = 0; value1D = 8L }; { i = 1; value1D = 9L } |]
        let separable2 = SparseArray2D.create (xValues2, yValues2)

        // Act
        let result = separable1 * separable2

        // Assert
        // Check the result is still separable
        match result with
        | SparseArray2D.SeparableSparseArr2D _ -> true.Should().BeTrue("Result should be separable") |> ignore
        | _ -> false.Should().BeTrue("Result should not be inseparable") |> ignore

        // Verify some values
        (result.tryFind 0 0).Value.Should().Be(2L * 6L * 4L * 8L) |> ignore // 384
        (result.tryFind 0 1).Value.Should().Be(2L * 6L * 5L * 9L) |> ignore // 540
        (result.tryFind 1 0).Value.Should().Be(3L * 7L * 4L * 8L) |> ignore // 672
        (result.tryFind 1 1).Value.Should().Be(3L * 7L * 5L * 9L) |> ignore // 945

    [<Fact>]
    let ``Separable array addition should produce inseparable result`` () =
        // Arrange
        let xValues1 = [| { i = 0; value1D = 2L }; { i = 1; value1D = 3L } |]
        let yValues1 = [| { i = 0; value1D = 4L }; { i = 1; value1D = 5L } |]
        let separable1 = SparseArray2D.create (xValues1, yValues1)

        let xValues2 = [| { i = 0; value1D = 6L }; { i = 1; value1D = 7L } |]
        let yValues2 = [| { i = 0; value1D = 8L }; { i = 1; value1D = 9L } |]
        let separable2 = SparseArray2D.create (xValues2, yValues2)

        // Act
        let result = separable1 + separable2

        // Assert
        // Check the result is inseparable
        match result with
        | SparseArray2D.InseparableSparseArr2D _ -> true.Should().BeTrue("Result should be inseparable") |> ignore
        | _ -> false.Should().BeTrue("Result should not be separable") |> ignore

        // Verify some values
        (result.tryFind 0 0).Value.Should().Be(2L * 4L + 6L * 8L) |> ignore // 8 + 48 = 56
        (result.tryFind 0 1).Value.Should().Be(2L * 5L + 6L * 9L) |> ignore // 10 + 54 = 64
        (result.tryFind 1 0).Value.Should().Be(3L * 4L + 7L * 8L) |> ignore // 12 + 56 = 68
        (result.tryFind 1 1).Value.Should().Be(3L * 5L + 7L * 9L) |> ignore // 15 + 63 = 78

    [<Fact>]
    let ``Inseparable and separable array multiplication works correctly`` () =
        // Arrange
        let xValues = [| { i = 0; value1D = 2L }; { i = 1; value1D = 3L } |]
        let yValues = [| { i = 0; value1D = 4L }; { i = 1; value1D = 5L } |]
        let separable = SparseArray2D.create (xValues, yValues)

        let inseparableValues = [|
            { i = 0; j = 0; value2D = 10L }
            { i = 0; j = 1; value2D = 20L }
            { i = 1; j = 0; value2D = 30L }
            { i = 1; j = 1; value2D = 40L }
        |]
        let inseparable = SparseArray2D.create inseparableValues

        // Act
        let result1 = separable * inseparable
        let result2 = inseparable * separable

        // Assert
        // Check both results are inseparable
        match result1 with
        | SparseArray2D.InseparableSparseArr2D _ -> true.Should().BeTrue("Result should be inseparable") |> ignore
        | _ -> false.Should().BeTrue("Result should not be separable") |> ignore

        match result2 with
        | SparseArray2D.InseparableSparseArr2D _ -> true.Should().BeTrue("Result should be inseparable") |> ignore
        | _ -> false.Should().BeTrue("Result should not be separable") |> ignore

        // Verify some values in result1 (separable * inseparable)
        (result1.tryFind 0 0).Value.Should().Be(2L * 4L * 10L) |> ignore // 80
        (result1.tryFind 0 1).Value.Should().Be(2L * 5L * 20L) |> ignore // 200
        (result1.tryFind 1 0).Value.Should().Be(3L * 4L * 30L) |> ignore // 360
        (result1.tryFind 1 1).Value.Should().Be(3L * 5L * 40L) |> ignore // 600

        // Verify the same values in result2 (inseparable * separable)
        (result2.tryFind 0 0).Value.Should().Be(10L * 2L * 4L) |> ignore // 80
        (result2.tryFind 0 1).Value.Should().Be(20L * 2L * 5L) |> ignore // 200
        (result2.tryFind 1 0).Value.Should().Be(30L * 3L * 4L) |> ignore // 360
        (result2.tryFind 1 1).Value.Should().Be(40L * 3L * 5L) |> ignore // 600

    [<Fact>]
    let ``Inseparable and separable array subtraction works correctly`` () =
        // Arrange
        let xValues = [| { i = 0; value1D = 2L }; { i = 1; value1D = 3L } |]
        let yValues = [| { i = 0; value1D = 4L }; { i = 1; value1D = 5L } |]
        let separable = SparseArray2D.create (xValues, yValues)

        let inseparableValues = [|
            { i = 0; j = 0; value2D = 10L }
            { i = 0; j = 1; value2D = 20L }
            { i = 1; j = 0; value2D = 30L }
            { i = 1; j = 1; value2D = 40L }
        |]
        let inseparable = SparseArray2D.create inseparableValues

        // Act
        let result1 = separable - inseparable
        let result2 = inseparable - separable

        // Assert
        // Check both results are inseparable
        match result1 with
        | SparseArray2D.InseparableSparseArr2D _ -> true.Should().BeTrue("Result should be inseparable")
        | _ -> false.Should().BeTrue("Result should not be separable")
        |> ignore

        match result2 with
        | SparseArray2D.InseparableSparseArr2D _ -> true.Should().BeTrue("Result should be inseparable")
        | _ -> false.Should().BeTrue("Result should not be separable")
        |> ignore

        // Verify some values in result1 (separable - inseparable)
        (result1.tryFind 0 0).Value.Should().Be(2L * 4L - 10L) |> ignore // 8 - 10 = -2
        (result1.tryFind 0 1).Value.Should().Be(2L * 5L - 20L) |> ignore // 10 - 20 = -10
        (result1.tryFind 1 0).Value.Should().Be(3L * 4L - 30L) |> ignore // 12 - 30 = -18
        (result1.tryFind 1 1).Value.Should().Be(3L * 5L - 40L) |> ignore // 15 - 40 = -25

        // Verify the same values in result2 (inseparable - separable)
        (result2.tryFind 0 0).Value.Should().Be(10L - 2L * 4L) |> ignore // 10 - 8 = 2
        (result2.tryFind 0 1).Value.Should().Be(20L - 2L * 5L) |> ignore // 20 - 10 = 10
        (result2.tryFind 1 0).Value.Should().Be(30L - 3L * 4L) |> ignore // 30 - 12 = 18
        (result2.tryFind 1 1).Value.Should().Be(40L - 3L * 5L) |> ignore // 40 - 15 = 25

    [<Fact>]
    let ``value() function works for both separable and inseparable arrays`` () =
        // Arrange
        // Create a separable array
        let xValues = [| { i = 0; value1D = 2L }; { i = 1; value1D = 3L } |]
        let yValues = [| { i = 0; value1D = 4L }; { i = 1; value1D = 5L } |]
        let separable = SparseArray2D.create (xValues, yValues)

        // Create an inseparable array
        let inseparableValues = [|
            { i = 0; j = 0; value2D = 10L }
            { i = 0; j = 1; value2D = 20L }
            { i = 1; j = 0; value2D = 30L }
            { i = 1; j = 1; value2D = 40L }
        |]
        let inseparable = SparseArray2D.create inseparableValues

        // Act
        let separableValues = separable.value()
        let inseparableValues = inseparable.value()

        // Assert
        // For separable, we expect the on-the-fly calculation to produce all combinations
        separableValues.Length.Should().Be(4) |> ignore

        // Check for specific values in the array
        let hasValue (i, j, v) =
            separableValues |> Array.exists (fun x -> x.i = i && x.j = j && x.value2D = v)

        hasValue(0, 0, 8L).Should().BeTrue() |> ignore
        hasValue(0, 1, 10L).Should().BeTrue() |> ignore
        hasValue(1, 0, 12L).Should().BeTrue() |> ignore
        hasValue(1, 1, 15L).Should().BeTrue() |> ignore

        // For inseparable, we just get the raw values
        inseparableValues.Length.Should().Be(4) |> ignore

        // Check that all inseparable values are present
        let insepHasValue (i, j, v) =
            inseparableValues |> Array.exists (fun x -> x.i = i && x.j = j && x.value2D = v)

        insepHasValue(0, 0, 10L).Should().BeTrue() |> ignore
        insepHasValue(0, 1, 20L).Should().BeTrue() |> ignore
        insepHasValue(1, 0, 30L).Should().BeTrue() |> ignore
        insepHasValue(1, 1, 40L).Should().BeTrue() |> ignore

    [<Fact>]
    let ``Separable arrays with complex patterns should multiply correctly`` () =
        // Arrange - create arrays with more interesting patterns
        let xValues1 = [|
            { i = 0; value1D = 2L }
            { i = 2; value1D = 3L }
            { i = 5; value1D = 4L }
        |]

        let yValues1 = [|
            { i = 1; value1D = 5L }
            { i = 3; value1D = 6L }
            { i = 7; value1D = 7L }
        |]

        let xValues2 = [|
            { i = 0; value1D = 8L }
            { i = 2; value1D = 9L }
            { i = 6; value1D = 10L }
        |]

        let yValues2 = [|
            { i = 1; value1D = 11L }
            { i = 4; value1D = 12L }
            { i = 7; value1D = 13L }
        |]

        let separable1 = SparseArray2D.create (xValues1, yValues1)
        let separable2 = SparseArray2D.create (xValues2, yValues2)

        // Act
        let result = separable1 * separable2

        // Assert
        // Check that result is separable
        match result with
        | SparseArray2D.SeparableSparseArr2D _ -> true.Should().BeTrue("Result should be separable") |> ignore
        | _ -> false.Should().BeTrue("Result should not be inseparable") |> ignore

        // Check that only values with matching indices exist in the result
        (result.tryFind 0 1).Value.Should().Be(2L * 8L * 5L * 11L) |> ignore // 880
        (result.tryFind 0 7).Value.Should().Be(2L * 8L * 7L * 13L) |> ignore // 1456
        (result.tryFind 2 1).Value.Should().Be(3L * 9L * 5L * 11L) |> ignore // 1485
        (result.tryFind 2 7).Value.Should().Be(3L * 9L * 7L * 13L) |> ignore // 2457

        // These shouldn't exist because the indices don't match in both arrays
        (result.tryFind 0 3).Should().BeNull() |> ignore
        (result.tryFind 0 4).Should().BeNull() |> ignore
        (result.tryFind 5 1).Should().BeNull() |> ignore
        (result.tryFind 6 7).Should().BeNull() |> ignore


    [<Fact>]
    let ``Empty sparse arrays should be handled correctly`` () =
        // Arrange
        let emptyData = array2D [
            [0L; 0L; 0L]
            [0L; 0L; 0L]
            [0L; 0L; 0L]
        ]

        let normalData = array2D [
            [1L; 2L; 3L]
            [4L; 5L; 6L]
            [7L; 8L; 9L]
        ]

        let emptySparse = TestHelpers.createSparseFrom2DArray emptyData
        let normalSparse = TestHelpers.createSparseFrom2DArray normalData

        // Act
        let additionResult = emptySparse + normalSparse
        let subtractionResult = normalSparse - emptySparse
        let multiplicationResult = emptySparse * normalSparse

        // Assert
        // Addition with empty should equal the normal array
        // Compare values of each position that should exist in normalSparse
        let normalValues = normalSparse.value()
        for v in normalValues do
            (additionResult.tryFind v.i v.j).Value.Should().Be(v.value2D) |> ignore

        // Multiplication with empty should be empty
        multiplicationResult.value().Length.Should().Be(0) |> ignore


    // Helper function to compare two sparse value arrays.
    let compareSparseArrays (tolerance: double) (arr1: SparseValue4D<double>[]) (arr2: SparseValue4D<double>[]) (label: string) =
        arr1.Length.Should().Be(arr2.Length, $"Both implementations should have the same number of elements in {label}") |> ignore

        use _ = new AssertionScope()

        for i in 0 .. arr1.Length - 1 do
            let sv1 = arr1.[i]
            let sv2 = arr2.[i]
            sv1.i.Should().Be(sv2.i, $"Index 'i' at position {i} in {label} should match") |> ignore
            sv1.j.Should().Be(sv2.j, $"Index 'j' at position {i} in {label} should match") |> ignore
            sv1.i1.Should().Be(sv2.i1, $"Index 'i1' at position {i} in {label} should match") |> ignore
            sv1.j1.Should().Be(sv2.j1, $"Index 'j1' at position {i} in {label} should match") |> ignore
            sv1.value4D.Should().BeApproximately(sv2.value4D, tolerance, $"Value at position {i} in {label} should be approximately equal") |> ignore


    // Define tolerance for float comparison.
    let tolerance = 1e-7


    let xDomainIntervals = DomainIntervals 5
    let yDomainIntervals = DomainIntervals 5


    let data1 =
        {
            xMutationProbabilityParams =
                {
                    domainParams =
                        {
                            domainIntervals = xDomainIntervals
                            domainRange = { minValue = -1.0; maxValue = 1.0  }
                        }
                    zeroThreshold = ZeroThreshold.defaultValue
                    epsFuncValue = 0.1 |> Eps0 |> ScalarEps
                }
            yMutationProbabilityParams =
                {
                    domainParams =
                        {
                            domainIntervals = yDomainIntervals
                            domainRange = { minValue = 0.0; maxValue = 5.0  }
                        }
                    zeroThreshold = ZeroThreshold.defaultValue
                    epsFuncValue = 0.15 |> Eps0 |> ScalarEps
                }
            sparseArrayType = StaticSparseArrayType
        }


    let data2 = { data1 with sparseArrayType = DynamicSparseArrayType }

    let result1 = MutationProbability4D.create DiscreteEvolution data1
    let result2 = MutationProbability4D.create DiscreteEvolution data2


    /// Compare the x1y1_xy fields.
    [<Fact>]
    let ``MutationProbability4D implementations should match x1y1_xy`` () =
        let sparseValues1 = result1.x1y1_xy.toSparseValueArray().value
        let sparseValues2 = result2.x1y1_xy.toSparseValueArray().value
        compareSparseArrays tolerance sparseValues1 sparseValues2 "x1y1_xy"


    /// Compare the xy_x1y1 fields.
    [<Fact>]
    let ``MutationProbability4D implementations should match xy_x1y1`` () =
        let sparseValues3 = result1.xy_x1y1.toSparseValueArray().value
        let sparseValues4 = result2.xy_x1y1.toSparseValueArray().value
        compareSparseArrays tolerance sparseValues3 sparseValues4 "xy_x1y1"
