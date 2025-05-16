namespace Softellect.Tests.MathTests

open Xunit
open FluentAssertions
open Softellect.Math.Primitives
open Softellect.Math.Sparse
open Xunit.Abstractions

module SparseArray2DVarianceTests =

    type SparseArray2DVarianceTests(output: ITestOutputHelper) =

        let domain2DRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 10
        let domain2D = Domain2D.create(intervals, domain2DRange)
        let convParams2D = conversionParameters2D domain2D

        let runTest2D (sparseArray : SparseArray<Point2D, double>) =
            let sparseArray2 = sparseArray.project convParams2D.projector
            let varianceMethod1 = sparseArray.variance convParams2D
            let varianceMethod2 = sparseArray2.variance (DistanceFunction convParams2D.arithmetic.distance)

            // Log results
            output.WriteLine($"Domain range: [{domain2DRange.minValue}, {domain2DRange.maxValue}]")
            output.WriteLine($"Intervals: {match intervals with DomainIntervals n -> n}")
            output.WriteLine($"Number of points: {sparseArray.values.Length}")
            output.WriteLine($"Variance method 1 (via conversion parameters): %A{varianceMethod1}")
            output.WriteLine($"Variance method 2 (via distance function): {varianceMethod2}")

            // Assert
            let var1 = varianceMethod1.x0 + varianceMethod1.x1
            var1.Should().BeApproximately(varianceMethod2, 1e-10) |> ignore

        [<Fact>]
        member _.``Both variance calculation methods should produce the same result for uniform 2D grid``() =
            // Create a sparse array with points on a uniform grid
            let points =
                [|
                    for i in 0..10 do
                        for j in 0..10 do
                            { x = { i0 = i; i1 = j }; value = 1.0 }
                |]

            let sparseArray = SparseArray.create points
            runTest2D sparseArray

        [<Fact>]
        member _.``Both variance calculation methods should produce the same result for sparse 2D data``() =
            // Create a sparse array with just a few points in 2D
            let points =
                [|
                    { x = { i0 = 1; i1 = 2 }; value = 3.5 }
                    { x = { i0 = 4; i1 = 7 }; value = 1.2 }
                    { x = { i0 = 8; i1 = 3 }; value = 2.7 }
                    { x = { i0 = 5; i1 = 5 }; value = 0.8 }
                |]

            let sparseArray = SparseArray.create points

            // Log additional information
            output.WriteLine($"Sparse 2D data points: {points.Length}")
            // output.WriteLine($"Data points: {System.String.Join(", ", points |> Array.map (fun p -> $"(({p.x.i0},{p.x.i1}), {p.value})"))}")

            runTest2D sparseArray

        [<Fact>]
        member _.``Both variance calculation methods should handle random 2D data``() =
            // Create a sparse array with random points
            let rng = System.Random(42)  // Seed for reproducibility
            let points =
                [|
                    for _ in 1..20 do
                        let i0 = rng.Next(0, 10)
                        let i1 = rng.Next(0, 10)
                        let value = rng.NextDouble() * 10.0
                        { x = { i0 = i0; i1 = i1 }; value = value }
                |]

            let sparseArray = SparseArray.create points

            // Log additional information
            output.WriteLine($"Random 2D data with {points.Length} points")
            output.WriteLine($"Effective non-zero points: {sparseArray.values.Length}")

            runTest2D sparseArray

        [<Fact>]
        member _.``Both variance calculation methods should handle edge case of single value in 2D``() =
            // Create a sparse array with just one point
            let points = [| { x = { i0 = 5; i1 = 5 }; value = 2.5 } |]

            let sparseArray = SparseArray.create points

            // Log additional information
            output.WriteLine($"Single data point at ({points[0].x.i0}, {points[0].x.i1}) with value {points[0].value}")
            let coord = convParams2D.projector points[0].x
            output.WriteLine($"Converted coordinate: ({coord.x0}, {coord.x1})")

            runTest2D sparseArray

        [<Fact>]
        member _.``Both variance calculation methods should handle edge case of zero values in 2D``() =
            // Create a sparse array with some zero values that should be filtered out
            let points =
                [|
                    { x = { i0 = 1; i1 = 2 }; value = 3.5 }
                    { x = { i0 = 4; i1 = 7 }; value = 0.0 }  // Should be filtered out
                    { x = { i0 = 8; i1 = 3 }; value = 2.7 }
                    { x = { i0 = 5; i1 = 5 }; value = 0.0 }  // Should be filtered out
                |]

            let sparseArray = SparseArray.create points

            // Log additional information
            output.WriteLine($"Original data points: {points.Length}")
            output.WriteLine($"Non-zero values: {sparseArray.values.Length}")

            runTest2D sparseArray

        [<Fact>]
        member _.``Both variance calculation methods should handle asymmetric 2D grid``() =
            // Create a sparse array with points on an asymmetric grid
            let points =
                [|
                    for i in 0..5 do
                        for j in 0..10 do
                            { x = { i0 = i; i1 = j }; value = 1.0 }
                |]

            let sparseArray = SparseArray.create points

            // Log additional information
            output.WriteLine($"Asymmetric 2D Grid (6×11)")
            output.WriteLine($"Number of points: {points.Length}")

            runTest2D sparseArray

        [<Fact>]
        member _.``Both variance calculation methods should handle 2D data with varying values``() =
            // Create a sparse array with varying values following a pattern
            let points =
                [|
                    for i in 0..10 do
                        for j in 0..10 do
                            // Create a gradient of values
                            let value = double(i + j) / 20.0
                            { x = { i0 = i; i1 = j }; value = value }
                |]

            let sparseArray = SparseArray.create points

            // Log additional information
            output.WriteLine($"2D grid with gradient values")
            output.WriteLine($"Value range: [0.0, 1.0]")
            output.WriteLine($"Number of points: {points.Length}")

            runTest2D sparseArray
