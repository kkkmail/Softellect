namespace Softellect.Tests.MathTests

open Xunit
open FluentAssertions
open Softellect.Math.Primitives
open Xunit.Abstractions

type CoordTests(output: ITestOutputHelper) =

    [<Fact>]
    member _.``Coord1D pown should calculate coordinate-wise power correctly``() =
        // Arrange
        let coord = { x0 = 2.0 }
        let expected = { x0 = 8.0 }

        // Act
        let actual = pown coord 3

        // Assert
        output.WriteLine($"Coord1D: {coord}^3 = {actual} (Expected: {expected})")
        actual.Should().BeEquivalentTo(expected, "because pown should raise each coordinate to the power") |> ignore

    [<Fact>]
    member _.``Coord2D pown should calculate coordinate-wise power correctly``() =
        // Arrange
        let coord = { x0 = 2.0; x1 = 3.0 }
        let expected = { x0 = 8.0; x1 = 27.0 }

        // Act
        let actual = pown coord 3

        // Assert
        output.WriteLine($"Coord2D: {coord}^3 = {actual} (Expected: {expected})")
        actual.Should().BeEquivalentTo(expected, "because pown should raise each coordinate to the power") |> ignore

    [<Fact>]
    member _.``Coord3D pown should calculate coordinate-wise power correctly``() =
        // Arrange
        let coord = { x0 = 2.0; x1 = 3.0; x2 = 4.0 }
        let expected = { x0 = 8.0; x1 = 27.0; x2 = 64.0 }

        // Act
        let actual = pown coord 3

        // Assert
        output.WriteLine($"Coord3D: {coord}^3 = {actual} (Expected: {expected})")
        actual.Should().BeEquivalentTo(expected, "because pown should raise each coordinate to the power") |> ignore

    [<Fact>]
    member _.``Arithmetic operators should work with Coord types``() =
        // Arrange
        let a = { x0 = 5.0; x1 = 10.0 }
        let b = { x0 = 2.0; x1 = 4.0 }

        // Act & Assert
        output.WriteLine($"Testing arithmetic operations on {a} and {b}")

        let sum = a + b
        output.WriteLine($"Addition: {a} + {b} = {sum}")
        sum.Should().BeEquivalentTo({ x0 = 7.0; x1 = 14.0 }, "because addition should be coordinate-wise") |> ignore

        let diff = a - b
        output.WriteLine($"Subtraction: {a} - {b} = {diff}")
        diff.Should().BeEquivalentTo({ x0 = 3.0; x1 = 6.0 }, "because subtraction should be coordinate-wise") |> ignore

        let product = a * b
        output.WriteLine($"Multiplication: {a} * {b} = {product}")
        product.Should().BeEquivalentTo({ x0 = 10.0; x1 = 40.0 }, "because multiplication should be coordinate-wise") |> ignore

        let division = a / b
        output.WriteLine($"Division: {a} / {b} = {division}")
        division.Should().BeEquivalentTo({ x0 = 2.5; x1 = 2.5 }, "because division should be coordinate-wise") |> ignore
