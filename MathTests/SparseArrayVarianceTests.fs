namespace Softellect.Tests.MathTests

open Xunit
open FluentAssertions
open Softellect.Math.Primitives
open Softellect.Math.Sparse
open Xunit.Abstractions

module SparseArrayVarianceTests =

    type SparseArrayVarianceTests(output: ITestOutputHelper) =

        let domain1DRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 10
        let domain1D = Domain.create(intervals, domain1DRange)
        let convParams1D = conversionParameters1D domain1D

        let runTest1D (sparseArray : SparseArray<Point1D, double>) =
            let sparseArray2 = sparseArray.project convParams1D.projector
            let varianceMethod1 = sparseArray.variance convParams1D
            let varianceMethod2 = sparseArray2.variance (DistanceFunction convParams1D.arithmetic.distance)

            // Log results
            output.WriteLine($"Domain range: [{domain1DRange.minValue}, {domain1DRange.maxValue}]")
            output.WriteLine($"Intervals: {match intervals with DomainIntervals n -> n}")
            output.WriteLine($"Number of points: {sparseArray.values.Length}")
            output.WriteLine($"Variance method 1 (via conversion parameters): %A{varianceMethod1}")
            output.WriteLine($"Variance method 2 (via distance function): {varianceMethod2}")

            // Assert
            varianceMethod1.x0.Should().BeApproximately(varianceMethod2, 1e-10) |> ignore

        [<Fact>]
        member _.``Both variance calculation methods should produce the same result for uniform 1D grid``() =
            // Create a sparse array with points on a uniform grid
            let points =
                [|
                    for i in 0..10 do
                        { x = { i0 = i }; value = 1.0 }
                |]

            let sparseArray = SparseArray.create points
            runTest1D sparseArray

        [<Fact>]
        member _.``Both variance calculation methods should produce the same result for sparse 1D data``() =
            // Create a sparse array with just a few points
            let points =
                [|
                    // { x = { i0 = 1 }; value = 3.5 }
                    // { x = { i0 = 4 }; value = 1.2 }
                    // { x = { i0 = 8 }; value = 2.7 }

                    { x = { i0 = 1 }; value = 10.0 }
                    { x = { i0 = 9 }; value = 10.0 }
                |]

            let sparseArray = SparseArray.create points

            // Log additional information
            output.WriteLine($"Sparse data points: {points.Length}")
            // output.WriteLine($"Data points: {System.String.Join(", ", points |> Array.map (fun p -> $"({p.x.i0}, {p.value})"))}")

            runTest1D sparseArray

        [<Fact>]
        member _.``Both variance calculation methods should handle random 1D data``() =
            // Create a sparse array with random points
            let rng = System.Random(42)  // Seed for reproducibility
            let points =
                [|
                    for _ in 1..15 do
                        let i = rng.Next(0, 10)
                        let value = rng.NextDouble() * 10.0
                        { x = { i0 = i }; value = value }
                |]

            let sparseArray = SparseArray.create points

            // Log additional information
            output.WriteLine($"Random 1D data with {points.Length} points")
            output.WriteLine($"Effective non-zero points: {sparseArray.values.Length}")

            runTest1D sparseArray

        [<Fact>]
        member _.``Both variance calculation methods should handle edge case of single value in 1D``() =
            // Create a sparse array with just one point
            let points = [| { x = { i0 = 5 }; value = 3.5 } |]

            let sparseArray = SparseArray.create points

            // Log additional information
            output.WriteLine($"Single point at index {points[0].x.i0}")
            output.WriteLine($"Converted coordinate: {convParams1D.projector points[0].x |> (fun c -> c.x0)}")

            runTest1D sparseArray

        [<Fact>]
        member _.``Both variance calculation methods should handle edge case of zero values in 1D``() =
            // Create a sparse array with some zero values that should be filtered out
            let points =
                [|
                    { x = { i0 = 1 }; value = 3.5 }
                    { x = { i0 = 4 }; value = 0.0 }  // Should be filtered out
                    { x = { i0 = 8 }; value = 2.7 }
                    { x = { i0 = 5 }; value = 0.0 }  // Should be filtered out
                |]

            let sparseArray = SparseArray.create points

            // Log additional information
            output.WriteLine($"Original data points: {points.Length}")
            output.WriteLine($"Non-zero values: {sparseArray.values.Length}")

            runTest1D sparseArray
