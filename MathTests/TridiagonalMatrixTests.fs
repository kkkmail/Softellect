namespace Softellect.Tests.MathTests

open Xunit
open FluentAssertions
open Softellect.Math.Primitives
open Softellect.Math.Sparse
open Softellect.Math.Tridiagonal
open Xunit.Abstractions

type TridiagonalMatrixTests(output: ITestOutputHelper) =
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

    // Tests for 2D Matrix
    [<Fact>]
    member _.``y_x probabilities should sum to 1 for all points in 2D matrix``() =
        // Arrange
        let d = 5 // Use a 5x5 grid
        let a = 0.4 // Use a probability of 0.4 for staying
        let matrix = createTridiagonalMatrix2D d a

        // Act & Assert
        // Test for each point in the grid
        for x0 in 0 .. d - 1 do
            for x1 in 0 .. d - 1 do
                let point = { x0 = x0; x1 = x1 }
                let values = matrix.y_x point

                // Sum up all probabilities from this point
                let sum = values.values |> Array.sumBy _.value

                // Should sum to 1.0 with small tolerance for floating-point errors
                sum.Should().BeApproximately(1.0, 1e-10) |> ignore

    [<Fact>]
    member _.``y_x probabilities should respect boundary conditions in 2D matrix``() =
        // Arrange
        let d = 3 // Use a small 3x3 grid to easily test all boundary conditions
        let a = 0.4 // Use a probability of 0.4 for staying
        let matrix = createTridiagonalMatrix2D d a

        // Case 1: Internal point (no boundaries)
        let internalPoint = { x0 = 1; x1 = 1 }
        let internalValues = matrix.y_x internalPoint
        internalValues.values.Length.Should().Be(5) |> ignore  // Self + 4 neighbors

        // Case 2: Point touching 1 boundary
        let boundary1Point = { x0 = 0; x1 = 1 }
        let boundary1Values = matrix.y_x boundary1Point
        boundary1Values.values.Length.Should().Be(4) |> ignore  // Self + 3 neighbors

        // Case 3: Corner point (touching 2 boundaries)
        let cornerPoint = { x0 = 0; x1 = 0 }
        let cornerValues = matrix.y_x cornerPoint
        cornerValues.values.Length.Should().Be(3) |> ignore  // Self + 2 neighbors

        // All should sum to 1.0
        [internalValues; boundary1Values; cornerValues]
        |> List.iter (fun values ->
            (values.values |> Array.sumBy _.value).Should().BeApproximately(1.0, 1e-10) |> ignore)

    [<Fact>]
    member _.``x_y and y_x should produce identical results in 2D matrix``() =
        // Arrange
        let d = 4 // Use a 4x4 grid
        let a = 0.3 // Use a probability of 0.3 for staying
        let matrix = createTridiagonalMatrix2D d a

        // Act & Assert
        // Test for several representative points
        let testPoints = [
            // Internal point
            { x0 = 1; x1 = 1 }
            // Point touching 1 boundary
            { x0 = 0; x1 = 1 }
            // Corner point (touching 2 boundaries)
            { x0 = 0; x1 = 0 }
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
    member _.``Moving probability should be correctly distributed based on boundaries in 2D matrix``() =
        // Arrange
        let d = 3
        let a = 0.1 // Small a to make differences more noticeable
        let matrix = createTridiagonalMatrix2D d a

        // Internal point has 4 neighbors, each should get equal probability
        let internalPoint = { x0 = 1; x1 = 1 }
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
        let boundaryPoint = { x0 = 0; x1 = 1 }
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

    // Tests for 4D Matrix
    [<Fact>]
    member _.``y_x probabilities should sum to 1 for all points in 4D matrix``() =
        // Arrange
        let d = 3 // Use a smaller grid due to the higher dimensionality
        let a = 0.4 // Use a probability of 0.4 for staying
        let matrix = createTridiagonalMatrix4D d a

        // Act & Assert
        // Test for each point in the grid
        for x0 in 0 .. d - 1 do
            for x1 in 0 .. d - 1 do
                for x2 in 0 .. d - 1 do
                    for x3 in 0 .. d - 1 do
                        let point = { x0 = x0; x1 = x1; x2 = x2; x3 = x3 }
                        let values = matrix.y_x point

                        // Sum up all probabilities from this point
                        let sum = values.values |> Array.sumBy _.value

                        // Should sum to 1.0 with small tolerance for floating-point errors
                        sum.Should().BeApproximately(1.0, 1e-10) |> ignore

    [<Fact>]
    member _.``y_x probabilities should respect boundary conditions in 4D matrix``() =
        // Arrange
        let d = 3 // Use a small 3x3x3x3 grid to easily test all boundary conditions
        let a = 0.4 // Use a probability of 0.4 for staying
        let matrix = createTridiagonalMatrix4D d a

        // Case 1: Internal point (no boundaries)
        let internalPoint = { x0 = 1; x1 = 1; x2 = 1; x3 = 1 }
        let internalValues = matrix.y_x internalPoint
        internalValues.values.Length.Should().Be(9) |> ignore  // Self + 8 neighbors

        // Case 2: Point touching 1 boundary
        let boundary1Point = { x0 = 0; x1 = 1; x2 = 1; x3 = 1 }
        let boundary1Values = matrix.y_x boundary1Point
        boundary1Values.values.Length.Should().Be(8) |> ignore  // Self + 7 neighbors

        // Case 3: Point touching 2 boundaries
        let boundary2Point = { x0 = 0; x1 = 0; x2 = 1; x3 = 1 }
        let boundary2Values = matrix.y_x boundary2Point
        boundary2Values.values.Length.Should().Be(7) |> ignore  // Self + 6 neighbors

        // Case 4: Point touching 3 boundaries
        let boundary3Point = { x0 = 0; x1 = 0; x2 = 0; x3 = 1 }
        let boundary3Values = matrix.y_x boundary3Point
        boundary3Values.values.Length.Should().Be(6) |> ignore  // Self + 5 neighbors

        // Case 5: Corner point (touching 4 boundaries)
        let cornerPoint = { x0 = 0; x1 = 0; x2 = 0; x3 = 0 }
        let cornerValues = matrix.y_x cornerPoint
        cornerValues.values.Length.Should().Be(5) |> ignore  // Self + 4 neighbors

        // All should sum to 1.0
        [internalValues; boundary1Values; boundary2Values; boundary3Values; cornerValues]
        |> List.iter (fun values ->
            (values.values |> Array.sumBy _.value).Should().BeApproximately(1.0, 1e-10) |> ignore)

    [<Fact>]
    member _.``x_y and y_x should produce identical results in 4D matrix``() =
        // Arrange
        let d = 3 // Use a 3x3x3x3 grid
        let a = 0.3 // Use a probability of 0.3 for staying
        let matrix = createTridiagonalMatrix4D d a

        // Act & Assert
        // Test for several representative points
        let testPoints = [
            // Internal point
            { x0 = 1; x1 = 1; x2 = 1; x3 = 1 }
            // Point touching 1 boundary
            { x0 = 0; x1 = 1; x2 = 1; x3 = 1 }
            // Point touching 2 boundaries
            { x0 = 0; x1 = 0; x2 = 1; x3 = 1 }
            // Point touching 3 boundaries
            { x0 = 0; x1 = 0; x2 = 0; x3 = 1 }
            // Corner point (touching 4 boundaries)
            { x0 = 0; x1 = 0; x2 = 0; x3 = 0 }
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

    // Tests for 5D Matrix
    [<Fact>]
    member _.``y_x probabilities should sum to 1 for all points in 5D matrix``() =
        // Arrange
        let d = 2 // Use a very small grid due to the high dimensionality
        let a = 0.4 // Use a probability of 0.4 for staying
        let matrix = createTridiagonalMatrix5D d a

        // Act & Assert
        // Test for each point in the grid
        for x0 in 0 .. d - 1 do
            for x1 in 0 .. d - 1 do
                for x2 in 0 .. d - 1 do
                    for x3 in 0 .. d - 1 do
                        for x4 in 0 .. d - 1 do
                            let point = { x0 = x0; x1 = x1; x2 = x2; x3 = x3; x4 = x4 }
                            let values = matrix.y_x point

                            // Sum up all probabilities from this point
                            let sum = values.values |> Array.sumBy _.value

                            // Should sum to 1.0 with small tolerance for floating-point errors
                            sum.Should().BeApproximately(1.0, 1e-10) |> ignore

    [<Fact>]
    member _.``y_x probabilities should respect boundary conditions in 5D matrix``() =
        // Arrange
        let d = 3 // Use a 3x3x3x3x3 grid to test boundary conditions
        let a = 0.4 // Use a probability of 0.4 for staying
        let matrix = createTridiagonalMatrix5D d a

        // Test various boundary conditions
        let testCases = [
            // Internal point (center of the grid)
            ({ x0 = 1; x1 = 1; x2 = 1; x3 = 1; x4 = 1 }, 11)  // Self + 10 neighbors

            // Point touching 1 boundary
            ({ x0 = 0; x1 = 1; x2 = 1; x3 = 1; x4 = 1 }, 10)  // Self + 9 neighbors

            // Point touching 2 boundaries
            ({ x0 = 0; x1 = 0; x2 = 1; x3 = 1; x4 = 1 }, 9)   // Self + 8 neighbors

            // Point touching 3 boundaries
            ({ x0 = 0; x1 = 0; x2 = 0; x3 = 1; x4 = 1 }, 8)   // Self + 7 neighbors

            // Point touching 4 boundaries
            ({ x0 = 0; x1 = 0; x2 = 0; x3 = 0; x4 = 1 }, 7)   // Self + 6 neighbors

            // Corner point (touching 5 boundaries)
            ({ x0 = 0; x1 = 0; x2 = 0; x3 = 0; x4 = 0 }, 6)   // Self + 5 neighbors
        ]

        // Verify each test case
        for (point, expectedLength) in testCases do
            let values = matrix.y_x point
            // Print the actual length for debugging
            output.WriteLine $"Point %A{point}: Expected {expectedLength}, Got {values.values.Length}"
            values.values.Length.Should().Be(expectedLength) |> ignore

            // Sum should be 1.0
            (values.values |> Array.sumBy _.value).Should().BeApproximately(1.0, 1e-10) |> ignore

    [<Fact>]
    member _.``Moving probability should be correctly distributed based on boundaries in 5D matrix``() =
        // Arrange
        let d = 3
        let a = 0.1 // Small a to make differences more noticeable
        let matrix = createTridiagonalMatrix5D d a

        // Internal point
        let internalPoint = { x0 = 1; x1 = 1; x2 = 1; x3 = 1; x4 = 1 }
        let internalValues = matrix.y_x internalPoint

        // Get the probability of moving to any neighbor for internal point
        let internalNeighborProb =
            internalValues.values
            |> Array.filter (fun v -> v.x <> internalPoint)
            |> Array.map _.value
            |> Array.head

        // All neighbor probabilities should be the same for internal point
        internalValues.values
        |> Array.filter (fun v -> v.x <> internalPoint)
        |> Array.iter (fun v -> v.value.Should().BeApproximately(internalNeighborProb, 1e-10) |> ignore)

        // Check probabilities for points with various boundary counts
        // The moving probability should increase as more boundaries are encountered
        let testPoints = [
            // Internal point (no boundaries)
            internalPoint
            // Point touching 1 boundary
            { x0 = 0; x1 = 1; x2 = 1; x3 = 1; x4 = 1 }
            // Point touching 2 boundaries
            { x0 = 0; x1 = 0; x2 = 1; x3 = 1; x4 = 1 }
        ]

        let mutable lastProb = 0.0

        for point in testPoints do
            let values = matrix.y_x point

            // Get stay probability (diagonal element)
            let stayProb =
                values.values
                |> Array.filter (fun v -> v.x = point)
                |> Array.map _.value
                |> Array.head

            // Get move probability (any neighbor)
            let moveProb =
                values.values
                |> Array.filter (fun v -> v.x <> point)
                |> Array.map _.value
                |> Array.head

            // All neighbors should have equal probability
            values.values
            |> Array.filter (fun v -> v.x <> point)
            |> Array.iter (fun v -> v.value.Should().BeApproximately(moveProb, 1e-10) |> ignore)

            // If not the first point, check that move probability is higher than for previous point
            if point <> internalPoint then
                moveProb.Should().BeGreaterThan(lastProb) |> ignore

            lastProb <- moveProb
