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
