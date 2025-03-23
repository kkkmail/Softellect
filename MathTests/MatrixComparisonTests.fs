namespace Softellect.Tests.MathTests

open Softellect.Math.Primitives
open Softellect.Math.Tridiagonal
open Xunit
open Xunit.Abstractions
open FluentAssertions
open Softellect.Math.Sparse

/// Test to compare old and new sparse matrix creation methods
[<Trait("Category", "Comparison")>]
type MatrixComparisonTests(output: ITestOutputHelper) =

    /// Helper to extract sparse values from an array
    let extractValues (sparseArray: SparseArray<'I, double>) =
        sparseArray.getValues()
        |> Seq.map (fun v -> (v.x, v.value))
        |> Seq.sortBy fst
        |> Seq.toArray

    /// Print sparse array contents
    let printSparseArray name (array: SparseArray<'I, double>) =
        output.WriteLine($"{name}:")
        let values = extractValues array
        for idx, value in values do
            output.WriteLine($"  {idx} -> {value}")
        output.WriteLine($"  Total elements: {values.Length}")

    /// Compare two sparse arrays and print differences
    let compareSparseArrays name1 (array1: SparseArray<'I, double>) name2 (array2: SparseArray<'I, double>) =
        let values1 = extractValues array1 |> Map.ofArray
        let values2 = extractValues array2 |> Map.ofArray

        let allKeys =
            Set.union (values1 |> Map.keys |> Set.ofSeq) (values2 |> Map.keys |> Set.ofSeq)
            |> Set.toArray
            |> Array.sort

        let diff() = output.WriteLine($"Comparing {name1} vs {name2}:")

        let mutable differences = 0

        for key in allKeys do
            let value1Opt = values1 |> Map.tryFind key
            let value2Opt = values2 |> Map.tryFind key

            match value1Opt, value2Opt with
            | None, None ->
                // Both arrays don't have this key (shouldn't happen)
                ()
            | Some v1, Some v2 when v1 = v2 ->
                // Both arrays have the same value for this key
                ()
            | Some v1, Some v2 ->
                // Both arrays have different values for this key
                diff()
                output.WriteLine($"  Different value for {key}: {name1}={v1}, {name2}={v2}")
                differences <- differences + 1
            | Some v1, None ->
                // Only array1 has this key
                diff()
                output.WriteLine($"  Key {key} present in {name1} with value {v1} but missing in {name2}")
                differences <- differences + 1
            | None, Some v2 ->
                // Only array2 has this key
                diff()
                output.WriteLine($"  Key {key} present in {name2} with value {v2} but missing in {name1}")
                differences <- differences + 1

        if differences <> 0 then output.WriteLine($"  Total differences: {differences} out of {allKeys.Length} keys")
        differences

    let toOldSparse2DArray (array: SparseArray<int[], float>) : SparseArray<Point2D, float> =
        array.getValues()
        |> Seq.map (fun v -> {x = { i0 = v.x[0]; i1 = v.x[1]}; value = v.value})
        |> Seq.toArray
        |> SparseArray.create

    [<Fact>]
    let ``Compare Old and New Matrix Creation Methods`` () =
        // Parameters for small test matrices
        let d = 5  // Small dimension size for easier debugging
        let k = 2  // 2D matrices for simplicity
        let a = 0.5 // Diagonal element value

        output.WriteLine($"Creating 2D matrices with d = {d} using old and new methods")

        // Create matrix using old method from MultidimensionalTests
        let oldMatrix = createTridiagonalMatrix2D d a

        // Create matrix using new method from MultidimensionalSparseTests
        let newMatrix = MultidimensionalSparseTests.Helpers.createTridiagonalMatrix d k a

        // Compare x_y results for a sample of points
        output.WriteLine("Comparing x_y results for sample points:")
        let mutable totalDifferences = 0

        for i in 0..(d-1) do
            for j in 0..(d-1) do
                let oldPoint : Point2D = { i0 = i; i1 = j }
                let newPoint = [|i; j|]

                let oldResult = oldMatrix.x_y oldPoint
                let newResult = newMatrix.x_y newPoint |> toOldSparse2DArray

                output.WriteLine($"Testing point ({i}, {j}):")
                let differences = compareSparseArrays "old x_y" oldResult "new x_y" newResult
                totalDifferences <- totalDifferences + differences

        // Compare y_x results for a sample of points
        output.WriteLine("Comparing y_x results for sample points:")
        totalDifferences <- 0

        for i in 0..(d-1) do
            for j in 0..(d-1) do
                let oldPoint : Point2D = { i0 = i; i1 = j }
                let newPoint = [|i; j|]

                let oldResult = oldMatrix.y_x oldPoint
                let newResult = newMatrix.y_x newPoint |> toOldSparse2DArray

                output.WriteLine($"Testing point ({i}, {j}):")
                let differences = compareSparseArrays "old y_x" oldResult "new y_x" newResult
                totalDifferences <- totalDifferences + differences

        // Create sample vectors
        let oldVector = MultidimensionalTests.Helpers.create2DHypersphereVector d 0.7 0.05
        let newVector = MultidimensionalSparseTests.Helpers.createHypersphereVector d k 0.7 0.05

        // Print vector details
        printSparseArray "Old vector" oldVector
        printSparseArray "New vector" (toOldSparse2DArray newVector)

        // Compare vectors
        let vectorDifferences = compareSparseArrays "new vector" (toOldSparse2DArray newVector) "old vector" oldVector

        // Perform multiplications
        let oldResult = oldMatrix * oldVector
        let newResult = newMatrix * newVector

        // Print result details
        printSparseArray "Old result" oldResult
        printSparseArray "New result" (toOldSparse2DArray newResult)

        // Compare results
        let resultDifferences = compareSparseArrays "new result" (toOldSparse2DArray newResult) "old result" oldResult

        // Summary
        output.WriteLine("==== Comparison Summary ====")
        output.WriteLine($"Matrix operation differences: {totalDifferences}")
        output.WriteLine($"Vector differences: {vectorDifferences}")
        output.WriteLine($"Result differences: {resultDifferences}")
        output.WriteLine($"Old result size: {extractValues(oldResult).Length}")
        output.WriteLine($"New result size: {extractValues(newResult).Length}")

        if resultDifferences > 0 || extractValues(oldResult).Length <> extractValues(newResult).Length then
            output.WriteLine("WARNING: The results differ significantly - this may explain the performance issues.")

        resultDifferences.Should().Be(0) |> ignore
