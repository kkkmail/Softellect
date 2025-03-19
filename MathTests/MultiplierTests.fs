namespace Softellect.Tests.MathTests

open Softellect.Math.Primitives
open Softellect.Math.Sparse
open Softellect.Math.Evolution
open Xunit
open Xunit.Abstractions
open FluentAssertions
open System

type MultiplierTests(output: ITestOutputHelper) =

    [<Fact>]
    member _.``sphericallySymmetric multiplier should compute correct values`` () =
        // Arrange
        let domain = Domain2D.create(DomainIntervals.defaultValue, DomainRange.defaultValue)

        // Define a simple function of r²: f(r²) = exp(-r²)
        let gaussianFunc (rSquared: double): double = Math.Exp(-rSquared)

        // Create multiplier
        let multiplier = Multiplier.sphericallySymmetric<Coord2D> (fun (p : Point2D) -> p.toCoord domain) gaussianFunc

        // Act & Assert

        // Test at origin (0,0) which is at index (50,50)
        let origin = { i0 = 50; i1 = 50 }
        let originValue = multiplier.invoke origin
        originValue.Should().BeApproximately(1.0, 1e-10, "at origin r² should be 0, so exp(-0) = 1") |> ignore

        // Test at point (1,0) which is at the edge
        let edgeX = { i0 = 100; i1 = 50 }
        let edgeXValue = multiplier.invoke edgeX
        edgeXValue.Should().BeApproximately(Math.Exp(-1.0), 1e-10, "at (1,0) r² should be 1, so exp(-1)") |> ignore

        // Test at point (0,1) which is at the edge
        let edgeY = { i0 = 50; i1 = 100 }
        let edgeYValue = multiplier.invoke edgeY
        edgeYValue.Should().BeApproximately(Math.Exp(-1.0), 1e-10, "at (0,1) r² should be 1, so exp(-1)") |> ignore

        // Test at point (1,1) which is at the corner
        let corner = { i0 = 100; i1 = 100 }
        let cornerValue = multiplier.invoke corner
        cornerValue.Should().BeApproximately(Math.Exp(-2.0), 1e-10, "at (1,1) r² should be 2, so exp(-2)") |> ignore

        // Verify symmetry - points at equal distances from origin should have equal values
        let point1 = { i0 = 60; i1 = 50 } // (some distance) along x-axis
        let point2 = { i0 = 50; i1 = 60 } // same distance along y-axis
        let value1 = multiplier.invoke point1
        let value2 = multiplier.invoke point2
        value1.Should().BeApproximately(value2, 1e-10, "multiplier should be spherically symmetric") |> ignore

        // Verify radial decay - points further from origin should have smaller values for our Gaussian
        let inner = { i0 = 60; i1 = 50 }
        let outer = { i0 = 70; i1 = 50 }
        let innerValue = multiplier.invoke inner
        let outerValue = multiplier.invoke outer
        innerValue.Should().BeGreaterThan(outerValue, "Gaussian function should decay with distance") |> ignore

    [<Fact>]
    member _.``identity multiplier should always return 1`` () =
        // Arrange
        let identityMultiplier = Multiplier.identity

        // Act & Assert
        let points = [
            { i0 = 0; i1 = 0 }      // corner (-1, -1)
            { i0 = 50; i1 = 50 }    // center (0, 0)
            { i0 = 100; i1 = 100 }  // corner (1, 1)
        ]

        for point in points do
            let value = identityMultiplier.invoke point
            value.Should().Be(1.0, $"identity multiplier should return 1.0 for all points, including {point}") |> ignore

    [<Fact>]
    member _.``sphericallySymmetric multiplier should handle custom functions`` () =
        // Arrange
        let domain = Domain2D.create(DomainIntervals.defaultValue, DomainRange.defaultValue)

        // Define a quadratic function of r²: f(r²) = 1 + 2r²
        let quadraticFunc (rSquared: double): double = 1.0 + 2.0 * rSquared

        // Create multiplier
        let multiplier = Multiplier.sphericallySymmetric<Coord2D> (fun (p : Point2D) -> p.toCoord domain) quadraticFunc

        // Act & Assert

        // Test at origin (0,0)
        let origin = { i0 = 50; i1 = 50 }
        let originValue = multiplier.invoke origin
        originValue.Should().BeApproximately(1.0, 1e-10, "at origin r² = 0, so 1 + 2(0) = 1") |> ignore

        // Test at point (1,0)
        let edgeX = { i0 = 100; i1 = 50 }
        let edgeXValue = multiplier.invoke edgeX
        edgeXValue.Should().BeApproximately(3.0, 1e-10, "at (1,0) r² = 1, so 1 + 2(1) = 3") |> ignore

        // Test at point (1,1)
        let corner = { i0 = 100; i1 = 100 }
        let cornerValue = multiplier.invoke corner
        cornerValue.Should().BeApproximately(5.0, 1e-10, "at (1,1) r² = 2, so 1 + 2(2) = 5") |> ignore
