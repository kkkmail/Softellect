namespace Softellect.Tests.MathTests

open Xunit
open FluentAssertions
open Softellect.Math.Sparse

/// Helper module to create test instances
module SparseTestHelpers =
    /// Create a sparse array with integer indices and values
    let createIntSparseArray(values: (int * int)[]) : SparseArray<int, int> =
        values
        |> Array.map (fun (idx, value) -> { x = idx; value = value })
        |> SparseArray.create

    /// Create a sparse array with string indices and float values
    let createStringSparseArray(values: (string * float)[]) : SparseArray<string, float> =
        values
        |> Array.map (fun (idx, value) -> { x = idx; value = value })
        |> SparseArray.create

    /// Create a simple sparse matrix with integer indices and values
    let createIntSparseMatrix() : SparseMatrix<int, int> =
        // Create a backing store for our test matrix
        let rows = [|
            (1, [|(2, 3); (3, 4)|])
            (2, [|(1, 5); (3, 6)|])
            (3, [|(1, 7); (2, 8)|])
        |]

        let columns = [|
            (1, [|(2, 5); (3, 7)|])
            (2, [|(1, 3); (3, 8)|])
            (3, [|(1, 4); (2, 6)|])
        |]

        let rowArrays =
            rows
            |> Array.map (fun (rowIdx, values) ->
                (rowIdx, createIntSparseArray values))
            |> Map.ofArray

        let colArrays =
            columns
            |> Array.map (fun (colIdx, values) ->
                (colIdx, createIntSparseArray values))
            |> Map.ofArray

        {
            x_y = fun x ->
                match rowArrays.TryFind x with
                | Some arr -> arr
                | None -> createIntSparseArray [||]

            y_x = fun y ->
                match colArrays.TryFind y with
                | Some arr -> arr
                | None -> createIntSparseArray [||]
        }

    /// Create a sparse matrix with string indices and float values
    let createStringSparseMatrix() : SparseMatrix<string, float> =
        // Create a backing store for our test matrix
        let rows = [|
            ("A", [|("X", 1.5); ("Y", 2.5)|])
            ("B", [|("X", 3.5); ("Z", 4.5)|])
            ("C", [|("Y", 5.5); ("Z", 6.5)|])
        |]

        let columns = [|
            ("X", [|("A", 1.5); ("B", 3.5)|])
            ("Y", [|("A", 2.5); ("C", 5.5)|])
            ("Z", [|("B", 4.5); ("C", 6.5)|])
        |]

        let rowArrays =
            rows
            |> Array.map (fun (rowIdx, values) ->
                (rowIdx, createStringSparseArray values))
            |> Map.ofArray

        let colArrays =
            columns
            |> Array.map (fun (colIdx, values) ->
                (colIdx, createStringSparseArray values))
            |> Map.ofArray

        {
            x_y = fun x ->
                match rowArrays.TryFind x with
                | Some arr -> arr
                | None -> createStringSparseArray [||]

            y_x = fun y ->
                match colArrays.TryFind y with
                | Some arr -> arr
                | None -> createStringSparseArray [||]
        }

open SparseTestHelpers

/// Tests for SparseMatrix multiplication
[<Trait("Category", "SparseMatrixMultiplication")>]
type SparseMatrixMultiplicationTests() =

    [<Fact>]
    let ``Matrix * Array - Integer indices and values``() =
        // Arrange
        let matrix = createIntSparseMatrix()
        let array = createIntSparseArray [|(1, 2); (2, 3); (3, 4)|]

        // Act
        let result = matrix * array

        // Assert
        // Expected calculations:
        // For y=1 in array (val=2):
        //   From matrix, y_x(1) gives us: (2->3), (3->4)
        //   So we get: 3*2=6 at x=2, 4*2=8 at x=3
        // For y=2 in array (val=3):
        //   From matrix, y_x(2) gives us: (1->5), (3->6)
        //   So we get: 5*3=15 at x=1, 6*3=18 at x=3
        // For y=3 in array (val=4):
        //   From matrix, y_x(3) gives us: (1->7), (2->8)
        //   So we get: 7*4=28 at x=1, 8*4=32 at x=2
        // Final result: x=1:(15+28)=43, x=2:(6+32)=38, x=3:(8+18)=26
        let expectedValues = [|(1, 43); (2, 38); (3, 26)|]
        let expected = createIntSparseArray expectedValues

        // Convert to sequences for comparison
        let resultSeq = result.getValues() |> Seq.map (fun v -> (v.x, v.value)) |> Seq.sortBy fst |> Seq.toArray
        let expectedSeq = expected.getValues() |> Seq.map (fun v -> (v.x, v.value)) |> Seq.sortBy fst |> Seq.toArray

        // Assert using FluentAssertions
        resultSeq.Should().BeEquivalentTo(expectedSeq)

    [<Fact>]
    let ``Array * Matrix - Integer indices and values``() =
        // Arrange
        let matrix = createIntSparseMatrix()
        let array = createIntSparseArray [|(1, 2); (2, 3); (3, 4)|]

        // Act
        let result = array * matrix

        // Assert
        // Expected calculations:
        // For x=1 in array (val=2):
        //   From matrix, x_y(1) gives us: (2->5), (3->7)
        //   So we get: 2*5=10 at y=2, 2*7=14 at y=3
        // For x=2 in array (val=3):
        //   From matrix, x_y(2) gives us: (1->3), (3->8)
        //   So we get: 3*3=9 at y=1, 3*8=24 at y=3
        // For x=3 in array (val=4):
        //   From matrix, x_y(3) gives us: (1->4), (2->6)
        //   So we get: 4*4=16 at y=1, 4*6=24 at y=2
        // Final result: y=1:(9+16)=25, y=2:(10+24)=34, y=3:(14+24)=38
        let expectedValues = [|(1, 25); (2, 34); (3, 38)|]
        let expected = createIntSparseArray expectedValues

        // Convert to sequences for comparison
        let resultSeq = result.getValues() |> Seq.map (fun v -> (v.x, v.value)) |> Seq.sortBy fst |> Seq.toArray
        let expectedSeq = expected.getValues() |> Seq.map (fun v -> (v.x, v.value)) |> Seq.sortBy fst |> Seq.toArray

        // Assert using FluentAssertions
        resultSeq.Should().BeEquivalentTo(expectedSeq)

    [<Fact>]
    let ``Matrix * Array - String indices and float values``() =
        // Arrange
        let matrix = createStringSparseMatrix()
        let array = createStringSparseArray [|("X", 1.0); ("Y", 2.0); ("Z", 3.0)|]

        // Act
        let result = matrix * array

        // Assert
        // Expected calculations:
        // For Y=X: (1.5*1.0) from X=A, (3.5*1.0) from X=B => X=A:1.5, X=B:3.5
        // For Y=Y: (2.5*2.0) from X=A, (5.5*2.0) from X=C => X=A:5.0, X=C:11.0
        // For Y=Z: (4.5*3.0) from X=B, (6.5*3.0) from X=C => X=B:13.5, X=C:19.5
        // Final result should be X=A:(1.5+5.0)=6.5, X=B:(3.5+13.5)=17.0, X=C:(11.0+19.5)=30.5
        let expectedValues = [|("A", 6.5); ("B", 17.0); ("C", 30.5)|]
        let expected = createStringSparseArray expectedValues

        // Convert to sequences for comparison
        let resultValues = result.getValues() |> Seq.map (fun v -> (v.x, v.value)) |> Seq.toArray
        let expectedValues = expected.getValues() |> Seq.map (fun v -> (v.x, v.value)) |> Seq.toArray

        // Assert with tolerance for floating point comparison
        resultValues.Length.Should().Be(expectedValues.Length) |> ignore

        for i in 0..(resultValues.Length - 1) do
            let (resultKey, resultValue) = resultValues.[i]
            let (expectedKey, expectedValue) = expectedValues.[i]

            resultKey.Should().Be(expectedKey) |> ignore
            resultValue.Should().BeApproximately(expectedValue, 0.0001) |> ignore

    [<Fact>]
    let ``Empty array multiplication returns empty array``() =
        // Arrange
        let matrix = createIntSparseMatrix()
        let emptyArray = createIntSparseArray [||]

        // Act
        let result = matrix * emptyArray

        // Assert
        let resultValues = result.getValues() |> Seq.toArray
        resultValues.Should().BeEmpty() |> ignore

    [<Fact>]
    let ``Multiplication with array containing non-existent indices in matrix``() =
        // Arrange
        let matrix = createIntSparseMatrix()
        let array = createIntSparseArray [|(4, 5); (5, 6)|] // Indices not present in matrix

        // Act
        let result = matrix * array

        // Assert
        let resultValues = result.getValues() |> Seq.toArray
        resultValues.Should().BeEmpty() |> ignore
