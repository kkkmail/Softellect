namespace Softellect.Tests.MathTests

open Xunit
open FluentAssertions
open Softellect.Math.Primitives
open Softellect.Math.Sparse
open Softellect.Math.Tridiagonal

type TridiagonalMatrixTests() =
    [<Fact>]
    member _.``y_x probabilities should sum to 1 for all points in 3D matrix``() =
        // Arrange
        let d = 5 // Use a 5x5x5 grid
        let a = 0.4 // Use a probability of 0.4 for staying
        let matrix = createTridiagonalMatrix3D d a

        // Act & Assert
        // Test for each point in the grid
        for x0 in 0 .. d - 1 do
            for x1 in 0 .. d - 1 do
                for x2 in 0 .. d - 1 do
                    let point = { x0 = x0; x1 = x1; x2 = x2 }
                    let values = matrix.y_x point

                    // Sum up all probabilities from this point
                    let sum = values.values |> Array.sumBy _.value

                    // Should sum to 1.0 with small tolerance for floating-point errors
                    sum.Should().BeApproximately(1.0, 1e-10) |> ignore

    [<Fact>]
    member _.``y_x probabilities should respect boundary conditions``() =
        // Arrange
        let d = 3 // Use a small 3x3x3 grid to easily test all boundary conditions
        let a = 0.4 // Use a probability of 0.4 for staying
        let matrix = createTridiagonalMatrix3D d a

        // Case 1: Internal point (no boundaries)
        let internalPoint = { x0 = 1; x1 = 1; x2 = 1 }
        let internalValues = matrix.y_x internalPoint
        internalValues.values.Length.Should().Be(7) |> ignore  // Self + 6 neighbors

        // Case 2: Point touching 1 boundary
        let boundary1Point = { x0 = 0; x1 = 1; x2 = 1 }
        let boundary1Values = matrix.y_x boundary1Point
        boundary1Values.values.Length.Should().Be(6) |> ignore  // Self + 5 neighbors

        // Case 3: Point touching 2 boundaries
        let boundary2Point = { x0 = 0; x1 = 0; x2 = 1 }
        let boundary2Values = matrix.y_x boundary2Point
        boundary2Values.values.Length.Should().Be(5) |> ignore  // Self + 4 neighbors

        // Case 4: Corner point (touching 3 boundaries)
        let cornerPoint = { x0 = 0; x1 = 0; x2 = 0 }
        let cornerValues = matrix.y_x cornerPoint
        cornerValues.values.Length.Should().Be(4) |> ignore  // Self + 3 neighbors

        // All should sum to 1.0
        [internalValues; boundary1Values; boundary2Values; cornerValues]
        |> List.iter (fun values ->
            (values.values |> Array.sumBy _.value).Should().BeApproximately(1.0, 1e-10) |> ignore)

    [<Fact>]
    member _.``x_y and y_x should produce identical results``() =
        // Arrange
        let d = 4 // Use a 4x4x4 grid
        let a = 0.3 // Use a probability of 0.3 for staying
        let matrix = createTridiagonalMatrix3D d a

        // Act & Assert
        // Test for several representative points
        let testPoints = [
            // Internal point
            { x0 = 1; x1 = 1; x2 = 1 }
            // Point touching 1 boundary
            { x0 = 0; x1 = 1; x2 = 1 }
            // Point touching 2 boundaries
            { x0 = 0; x1 = 0; x2 = 1 }
            // Corner point (touching 3 boundaries)
            { x0 = 0; x1 = 0; x2 = 0 }
        ]

        for point in testPoints do
            let x_y_values = matrix.x_y point
            let y_x_values = matrix.y_x point

            // Compare length
            x_y_values.values.Length.Should().Be(y_x_values.values.Length) |> ignore

            // Compare each value
            for i in 0 .. x_y_values.values.Length - 1 do
                let xyValue = x_y_values.values[i]
                let yxValue = y_x_values.values[i]

                // Points should be the same
                xyValue.x.Should().Be(yxValue.x) |> ignore

                // Values should be the same
                xyValue.value.Should().BeApproximately(yxValue.value, 1e-10) |> ignore

    [<Fact>]
    member _.``Moving probability should be correctly distributed based on boundaries``() =
        // Arrange
        let d = 3
        let a = 0.1 // Small a to make differences more noticeable
        let matrix = createTridiagonalMatrix3D d a

        // Internal point has 6 neighbors, each should get equal probability
        let internalPoint = { x0 = 1; x1 = 1; x2 = 1 }
        let internalValues = matrix.y_x internalPoint

        // Get the probability of moving to any neighbor
        let neighborProb =
            internalValues.values
            |> Array.filter (fun v -> v.x <> internalPoint)
            |> Array.map _.value
            |> Array.head

        // All neighbor probabilities should be the same for internal point
        internalValues.values
        |> Array.filter (fun v -> v.x <> internalPoint)
                    |> Array.iter (fun v -> v.value.Should().BeApproximately(neighborProb, 1e-10) |> ignore)

        // For a point on one boundary, neighbors should also have equal probabilities
        // but higher than for internal points
        let boundaryPoint = { x0 = 0; x1 = 1; x2 = 1 }
        let boundaryValues = matrix.y_x boundaryPoint

        let boundaryNeighborProb =
            boundaryValues.values
            |> Array.filter (fun v -> v.x <> boundaryPoint)
            |> Array.map _.value
            |> Array.head

        // Boundary neighbor prob should be higher than internal neighbor prob
        boundaryNeighborProb.Should().BeGreaterThan(neighborProb) |> ignore

        // All boundary point neighbors should have equal probability
        boundaryValues.values
        |> Array.filter (fun v -> v.x <> boundaryPoint)
                    |> Array.iter (fun v -> v.value.Should().BeApproximately(boundaryNeighborProb, 1e-10) |> ignore)
