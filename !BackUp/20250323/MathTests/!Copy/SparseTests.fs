﻿namespace Softellect.Tests.MathTests

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

    /// Create a sparse matrix from a list of triples (x, y, value)
    let inline createMatrixFromTriples<'I, 'T
            when ^I: equality
            and ^I: comparison
            and ^T: (static member ( * ) : ^T * ^T -> ^T)
            and ^T: (static member ( + ) : ^T * ^T -> ^T)
            and ^T: (static member ( - ) : ^T * ^T -> ^T)
            and ^T: (static member Zero : ^T)
            and ^T: equality
            and ^T: comparison>
            (triples: ('I * 'I * 'T)[])
            (createSparseArray: ('I * 'T)[] -> SparseArray<'I, 'T>) : SparseMatrix<'I, 'T> =

        // Group by x to create x_y function
        let byX =
            triples
            |> Array.groupBy (fun (x, _, _) -> x)
            |> Array.map (fun (x, group) ->
                let values = group |> Array.map (fun (_, y, v) -> (y, v))
                (x, createSparseArray values))
            |> Map.ofArray

        // Group by y to create y_x function
        let byY =
            triples
            |> Array.groupBy (fun (_, y, _) -> y)
            |> Array.map (fun (y, group) ->
                let values = group |> Array.map (fun (x, _, v) -> (x, v))
                (y, createSparseArray values))
            |> Map.ofArray

        {
            x_y = fun x ->
                match byX.TryFind x with
                | Some arr -> arr
                | None -> createSparseArray [||]

            y_x = fun y ->
                match byY.TryFind y with
                | Some arr -> arr
                | None -> createSparseArray [||]
        }

    /// Create a simple sparse matrix with integer indices and values
    let createIntSparseMatrix() : SparseMatrix<int, int> =
        // Matrix data as triples (x, y, value)
        let triples = [|
            // Row 1 (x=1)
            (1, 2, 3)   // x=1, y=2, value=3
            (1, 3, 4)   // x=1, y=3, value=4

            // Row 2 (x=2)
            (2, 1, 3)   // x=2, y=1, value=3
            (2, 3, 8)   // x=2, y=3, value=8

            // Row 3 (x=3)
            (3, 1, 4)   // x=3, y=1, value=4
            (3, 2, 6)   // x=3, y=2, value=6
        |]

        createMatrixFromTriples triples createIntSparseArray

    /// Create a sparse matrix with string indices and float values
    let createStringSparseMatrix() : SparseMatrix<string, float> =
        // Matrix data as triples (x, y, value)
        let triples = [|
            ("A", "X", 1.5)
            ("A", "Y", 2.5)
            ("B", "X", 3.5)
            ("B", "Z", 4.5)
            ("C", "Y", 5.5)
            ("C", "Z", 6.5)
        |]

        createMatrixFromTriples triples createStringSparseArray

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
        // For the matrix with entries:
        // (1,2,3), (1,3,4), (2,1,3), (2,3,8), (3,1,4), (3,2,6)
        // and array with entries:
        // (1,2), (2,3), (3,4)
        // Result:
        // For array y=1, val=2: matrix y_x(1) gives (2->3),(3->4), so: 3*2=6 at x=2, 4*2=8 at x=3
        // For array y=2, val=3: matrix y_x(2) gives (1->3), so: 3*3=9 at x=1
        // For array y=3, val=4: matrix y_x(3) gives (1->4),(2->8), so: 4*4=16 at x=1, 8*4=32 at x=2
        // Final: x=1:(9+16)=25, x=2:(6+32)=38, x=3:(4+18)=26
        let expectedValues = [|(1, 25); (2, 38); (3, 26)|]
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
        // For the matrix with entries:
        // (1,2,3), (1,3,4), (2,1,3), (2,3,8), (3,1,4), (3,2,6)
        // and array with entries:
        // (1,2), (2,3), (3,4)
        // Result:
        // For array x=1, val=2: matrix x_y(1) gives (2->3),(3->4), so: 2*3=6 at y=2, 2*4=8 at y=3
        // For array x=2, val=3: matrix x_y(2) gives (1->3),(3->8), so: 3*3=9 at y=1, 3*8=24 at y=3
        // For array x=3, val=4: matrix x_y(3) gives (1->4),(2->6), so: 4*4=16 at y=1, 4*6=24 at y=2
        // Final: y=1:(9+16)=25, y=2:(6+24)=30, y=3:(8+24)=32
        let expectedValues = [|(1, 25); (2, 30); (3, 32)|]
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
        let resultSeq = result.getValues() |> Seq.map (fun v -> (v.x, v.value)) |> Seq.sortBy fst |> Seq.toArray
        let expectedSeq = expected.getValues() |> Seq.map (fun v -> (v.x, v.value)) |> Seq.sortBy fst |> Seq.toArray

        // Assert with tolerance for floating point comparison
        resultSeq.Length.Should().Be(expectedSeq.Length) |> ignore

        for i in 0..(resultSeq.Length - 1) do
            let (resultKey, resultValue) = resultSeq.[i]
            let (expectedKey, expectedValue) = expectedSeq.[i]

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
        resultValues.Should().BeEmpty()

    [<Fact>]
    let ``Multiplication with array containing non-existent indices in matrix``() =
        // Arrange
        let matrix = createIntSparseMatrix()
        let array = createIntSparseArray [|(4, 5); (5, 6)|] // Indices not present in matrix

        // Act
        let result = matrix * array

        // Assert
        let resultValues = result.getValues() |> Seq.toArray
        resultValues.Should().BeEmpty()
