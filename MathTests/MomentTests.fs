namespace Softellect.Tests.MathTests

open Xunit
open FluentAssertions
open Softellect.Math.Primitives
open Softellect.Math.Sparse
open Xunit.Abstractions

type MomentTests(output: ITestOutputHelper) =

    [<Fact>]
    member _.``1D SparseArray moment calculations should be correct``() =
        // Arrange
        // Create domain in [-1, 1] interval
        let domainRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 10
        let domain = Domain.create(intervals, domainRange)

        // Create a sparse array with points
        let sparseValues =
            [|
                { x = { i0 = 0 }; value = 1L }   // -1.0
                { x = { i0 = 5 }; value = 3L }   //  0.0
                { x = { i0 = 10 }; value = 1L }  //  1.0
            |]

        let sparseArray = SparseArray.create sparseValues

        // The projector function to map point to coordinate
        let projector (p: Point1D) = p.toCoord domain

        // Act
        let moment0 = sparseArray.moment double projector 0
        let moment1 = sparseArray.moment double projector 1
        let moment2 = sparseArray.moment double projector 2

        // Assert
        output.WriteLine($"Domain: [{domain.domainRange.minValue}, {domain.domainRange.maxValue}]")
        // output.WriteLine($"Points: {String.Join(", ", sparseValues |> Array.map (fun v -> $"({v.x.i0}: {v.value})"))}")
        output.WriteLine($"Moment 0: {moment0}")
        output.WriteLine($"Moment 1: {moment1}")
        output.WriteLine($"Moment 2: {moment2}")

        // Verify calculations
        // Moment 0 is always 1 (it's the sum of probabilities)
        moment0.Should().Be(Coord1D.One, "because the 0th moment should always be 1") |> ignore

        // Moment 1 - expected value calculation
        // (-1.0*1 + 0.0*3 + 1.0*1) / 5 = 0.0
        moment1.Should().BeEquivalentTo({ x0 = 0.0 }, "because that's the weighted average of coordinates") |> ignore

        // Moment 2 - second moment (raw, not central)
        // ((-1.0)^2*1 + (0.0)^2*3 + (1.0)^2*1) / 5 = 0.4
        moment2.Should().BeEquivalentTo({ x0 = 0.4 }, "because that's the weighted average of squared coordinates") |> ignore


    // [<Fact>]
    // member _.``2D SparseArray moment calculations should be correct``() =
    //     // Arrange
    //     // Create domain in [-1, 1] interval
    //     let domainRange = { minValue = -1.0; maxValue = 1.0 }
    //     let intervals = DomainIntervals 10
    //     let domain = Domain2D.create(intervals, domainRange)
    //
    //     // Create a sparse array with points
    //     let sparseValues = [|
    //         { x = { i0 = 0; i1 = 0 }; value = 1L }      // (-1.0, -1.0)
    //         { x = { i0 = 5; i1 = 5 }; value = 4L }      // (0.0, 0.0)
    //         { x = { i0 = 10; i1 = 10 }; value = 1L }    // (1.0, 1.0)
    //         { x = { i0 = 0; i1 = 10 }; value = 2L }     // (-1.0, 1.0)
    //         { x = { i0 = 10; i1 = 0 }; value = 2L }     // (1.0, -1.0)
    //     |]
    //
    //     let sparseArray = SparseArray.create sparseValues
    //
    //     // The projector function to map point to coordinate
    //     let projector (p: Point2D) = p.toCoord domain
    //
    //     // Act
    //     let moment0 = sparseArray.moment toDouble projector 0
    //     let moment1 = sparseArray.moment toDouble projector 1
    //     let moment2 = sparseArray.moment toDouble projector 2
    //
    //     // Calculate expected values
    //     let totalWeight = 10.0 // Sum of all weights
    //
    //     // Expected coordinates with their weights
    //     let coords = [
    //         { x0 = -1.0; x1 = -1.0 }, 1.0    // Point (0,0) with weight 1
    //         { x0 = 0.0; x1 = 0.0 }, 4.0      // Point (5,5) with weight 4
    //         { x0 = 1.0; x1 = 1.0 }, 1.0      // Point (10,10) with weight 1
    //         { x0 = -1.0; x1 = 1.0 }, 2.0     // Point (0,10) with weight 2
    //         { x0 = 1.0; x1 = -1.0 }, 2.0     // Point (10,0) with weight 2
    //     ]
    //
    //     // Calculate expected moments
    //     let expectedM1 =
    //         coords
    //         |> List.fold (fun (sumX0, sumX1) (coord, weight) ->
    //             (sumX0 + coord.x0 * weight, sumX1 + coord.x1 * weight))
    //             (0.0, 0.0)
    //         |> fun (x0, x1) -> { x0 = x0 / totalWeight; x1 = x1 / totalWeight }
    //
    //     let expectedM2 =
    //         coords
    //         |> List.fold (fun (sumX0, sumX1) (coord, weight) ->
    //             (sumX0 + coord.x0 * coord.x0 * weight, sumX1 + coord.x1 * coord.x1 * weight))
    //             (0.0, 0.0)
    //         |> fun (x0, x1) -> { x0 = x0 / totalWeight; x1 = x1 / totalWeight }
    //
    //     // Assert
    //     output.WriteLine($"Domain: [{domain.d0.domainRange.minValue}, {domain.d0.domainRange.maxValue}] x [{domain.d1.domainRange.minValue}, {domain.d1.domainRange.maxValue}]")
    //     output.WriteLine($"Points: {String.Join(", ", sparseValues |> Array.map (fun v -> $"(({v.x.i0},{v.x.i1}): {v.value})"))}")
    //     output.WriteLine($"Moment 0: {moment0}")
    //     output.WriteLine($"Moment 1: {moment1}")
    //     output.WriteLine($"Expected Moment 1: {expectedM1}")
    //     output.WriteLine($"Moment 2: {moment2}")
    //     output.WriteLine($"Expected Moment 2: {expectedM2}")
    //
    //     // Verify calculations
    //     moment0.Should().BeEquivalentTo(Coord2D.One, "because the 0th moment should always be 1")
    //     moment1.Should().BeApproximatelyEquivalentTo(expectedM1, 1e-10, "because that's the weighted average of coordinates")
    //     moment2.Should().BeApproximatelyEquivalentTo(expectedM2, 1e-10, "because that's the weighted average of squared coordinates")
