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
        let conversionParameters = conversionParameters1D domain

        // Act
        let moment0 = sparseArray.moment conversionParameters 0
        let moment1 = sparseArray.moment conversionParameters 1
        let moment2 = sparseArray.moment conversionParameters 2

        // Assert
        output.WriteLine($"Domain: [{domain.domainRange.minValue}, {domain.domainRange.maxValue}]")
        output.WriteLine($"Moment 0: {moment0}")
        output.WriteLine($"Moment 1: {moment1}")
        output.WriteLine($"Moment 2: {moment2}")

        // Verify calculations
        // Moment 0 is always 1 (it's the sum of probabilities)
        let diff0 = (moment0 - Coord1D.One)
        diff0.total().Should().BeApproximately(0.0, 1e-10, "because the 0th moment should always be 1") |> ignore

        // Moment 1 - expected value calculation
        // (-1.0*1 + 0.0*3 + 1.0*1) / 5 = 0.0
        let expected1 = { x0 = 0.0 }
        let diff1 = (moment1 - expected1)
        diff1.total().Should().BeApproximately(0.0, 1e-10, "because that's the weighted average of coordinates") |> ignore

        // Moment 2 - second moment (raw, not central)
        // ((-1.0)^2*1 + (0.0)^2*3 + (1.0)^2*1) / 5 = 0.4
        let expected2 = { x0 = 0.4 }
        let diff2 = (moment2 - expected2)
        diff2.total().Should().BeApproximately(0.0, 1e-10, "because that's the weighted average of squared coordinates") |> ignore

    [<Fact>]
    member _.``2D SparseArray moment calculations should be correct``() =
        // Arrange
        // Create domain in [-1, 1] interval
        let domainRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 10
        let domain = Domain2D.create(intervals, domainRange)

        // Create a sparse array with points
        let sparseValues =
            [|
                { x = { i0 = 0; i1 = 0 }; value = 1L }      // (-1.0, -1.0)
                { x = { i0 = 5; i1 = 5 }; value = 4L }      // (0.0, 0.0)
                { x = { i0 = 10; i1 = 10 }; value = 1L }    // (1.0, 1.0)
                { x = { i0 = 0; i1 = 10 }; value = 2L }     // (-1.0, 1.0)
                { x = { i0 = 10; i1 = 0 }; value = 2L }     // (1.0, -1.0)
            |]

        let sparseArray = SparseArray.create sparseValues
        let conversionParameters = conversionParameters2D domain

        // Act
        let moment0 = sparseArray.moment conversionParameters 0
        let moment1 = sparseArray.moment conversionParameters 1
        let moment2 = sparseArray.moment conversionParameters 2

        // Assert
        output.WriteLine($"Domain: [{domain.d0.domainRange.minValue}, {domain.d0.domainRange.maxValue}] x [{domain.d1.domainRange.minValue}, {domain.d1.domainRange.maxValue}]")
        output.WriteLine($"Moment 0: {moment0}")
        output.WriteLine($"Moment 1: {moment1}")
        output.WriteLine($"Moment 2: {moment2}")

        // Verify calculations
        // Moment 0 is always 1 (the sum of probabilities)
        let diff0 = (moment0 - Coord2D.One)
        diff0.total().Should().BeApproximately(0.0, 1e-10, "because the 0th moment should always be 1") |> ignore

        // Moment 1 - expected value calculation for x0 and x1
        // For x0: (-1.0*1 + 0.0*4 + 1.0*1 + (-1.0)*2 + 1.0*2) / 10 = 0.0
        // For x1: (-1.0*1 + 0.0*4 + 1.0*1 + 1.0*2 + (-1.0)*2) / 10 = 0.0
        let expected1 = { x0 = 0.0; x1 = 0.0 }
        let diff1 = (moment1 - expected1)
        diff1.total().Should().BeApproximately(0.0, 1e-10, "because that's the weighted average of coordinates") |> ignore

        // Moment 2 - second moment (raw, not central)
        // For x0: ((-1.0)^2*1 + (0.0)^2*4 + (1.0)^2*1 + (-1.0)^2*2 + (1.0)^2*2) / 10 = 0.6
        // For x1: ((-1.0)^2*1 + (0.0)^2*4 + (1.0)^2*1 + (1.0)^2*2 + (-1.0)^2*2) / 10 = 0.6
        let expected2 = { x0 = 0.6; x1 = 0.6 }
        let diff2 = (moment2 - expected2)
        diff2.total().Should().BeApproximately(0.0, 1e-10, "because that's the weighted average of squared coordinates") |> ignore

    [<Fact>]
    member _.``3D SparseArray moment calculations should be correct``() =
        // Arrange
        // Create domain in [-1, 1] interval
        let domainRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 10
        let domain = Domain3D.create(intervals, domainRange)

        // Create a sparse array with points
        let sparseValues =
            [|
                { x = { i0 = 0; i1 = 0; i2 = 0 }; value = 1L }           // (-1.0, -1.0, -1.0)
                { x = { i0 = 5; i1 = 5; i2 = 5 }; value = 8L }           // (0.0, 0.0, 0.0)
                { x = { i0 = 10; i1 = 10; i2 = 10 }; value = 1L }        // (1.0, 1.0, 1.0)
                { x = { i0 = 0; i1 = 10; i2 = 5 }; value = 2L }          // (-1.0, 1.0, 0.0)
                { x = { i0 = 10; i1 = 0; i2 = 5 }; value = 2L }          // (1.0, -1.0, 0.0)
                { x = { i0 = 5; i1 = 0; i2 = 10 }; value = 1L }          // (0.0, -1.0, 1.0)
                { x = { i0 = 5; i1 = 10; i2 = 0 }; value = 1L }          // (0.0, 1.0, -1.0)
            |]

        let sparseArray = SparseArray.create sparseValues
        let conversionParameters = conversionParameters3D domain

        // Act
        let moment0 = sparseArray.moment conversionParameters 0
        let moment1 = sparseArray.moment conversionParameters 1
        let moment2 = sparseArray.moment conversionParameters 2

        // Assert
        output.WriteLine($"Domain 3D with range: [{domain.d0.domainRange.minValue}, {domain.d0.domainRange.maxValue}]")
        output.WriteLine($"Moment 0: {moment0}")
        output.WriteLine($"Moment 1: {moment1}")
        output.WriteLine($"Moment 2: {moment2}")

        // Total weight = 16

        // Verify calculations
        // Moment 0 is always 1 (the sum of probabilities)
        let diff0 = (moment0 - Coord3D.One)
        diff0.total().Should().BeApproximately(0.0, 1e-10, "because the 0th moment should always be 1") |> ignore

        // Moment 1 calculations
        // Calculated expected values - symmetrically distributed, expect 0.0 for all dimensions
        let expected1 = { x0 = 0.0; x1 = 0.0; x2 = 0.0 }
        let diff1 = (moment1 - expected1)
        diff1.total().Should().BeApproximately(0.0, 1e-10, "because the 1st moment should be the weighted average of coordinates") |> ignore

        // Moment 2 calculations
        // For x0: ((-1.0)^2*1 + (0.0)^2*8 + (1.0)^2*1 + (-1.0)^2*2 + (1.0)^2*2 + (0.0)^2*1 + (0.0)^2*1) / 16 = 0.375
        // For x1: ((-1.0)^2*1 + (0.0)^2*8 + (1.0)^2*1 + (1.0)^2*2 + (-1.0)^2*2 + (-1.0)^2*1 + (1.0)^2*1) / 16 = 0.5
        // For x2: ((-1.0)^2*1 + (0.0)^2*8 + (1.0)^2*1 + (0.0)^2*2 + (0.0)^2*2 + (1.0)^2*1 + (-1.0)^2*1) / 16 = 0.25
        let expected2 = { x0 = 0.375; x1 = 0.5; x2 = 0.25 }
        let diff2 = (moment2 - expected2)
        diff2.total().Should().BeApproximately(0.0, 1e-10, "because the 2nd moment should be the weighted average of squared coordinates") |> ignore

    [<Fact>]
    member _.``4D SparseArray moment calculations should be correct``() =
        // Arrange
        // Create domain in [-1, 1] interval
        let domainRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 10
        let domain = Domain4D.create(intervals, domainRange)

        // Create a sparse array with points
        let sparseValues =
            [|
                { x = { i0 = 0; i1 = 0; i2 = 0; i3 = 0 }; value = 1L }               // all -1.0
                { x = { i0 = 5; i1 = 5; i2 = 5; i3 = 5 }; value = 4L }               // all 0.0
                { x = { i0 = 10; i1 = 10; i2 = 10; i3 = 10 }; value = 1L }           // all 1.0
                { x = { i0 = 0; i1 = 10; i2 = 5; i3 = 0 }; value = 2L }              // (-1.0, 1.0, 0.0, -1.0)
                { x = { i0 = 10; i1 = 0; i2 = 5; i3 = 10 }; value = 2L }             // (1.0, -1.0, 0.0, 1.0)
            |]

        let sparseArray = SparseArray.create sparseValues
        let conversionParameters = conversionParameters4D domain

        // Act
        let moment0 = sparseArray.moment conversionParameters 0
        let moment1 = sparseArray.moment conversionParameters 1
        let moment2 = sparseArray.moment conversionParameters 2

        // Assert
        output.WriteLine($"Domain 4D with range: [{domain.d0.domainRange.minValue}, {domain.d0.domainRange.maxValue}]")
        output.WriteLine($"Moment 0: {moment0}")
        output.WriteLine($"Moment 1: {moment1}")
        output.WriteLine($"Moment 2: {moment2}")

        // Total weight = 10

        // Verify calculations
        // Moment 0 is always 1 (the sum of probabilities)
        let diff0 = (moment0 - Coord4D.One)
        diff0.total().Should().BeApproximately(0.0, 1e-10, "because the 0th moment should always be 1") |> ignore

        // Moment 1 calculations
        // For all dimensions: symmetrically distributed, expect 0.0
        let expected1 = { x0 = 0.0; x1 = 0.0; x2 = 0.0; x3 = 0.0 }
        let diff1 = (moment1 - expected1)
        diff1.total().Should().BeApproximately(0.0, 1e-10, "because the 1st moment should be the weighted average of coordinates") |> ignore

        // Moment 2 calculations for each dimension
        // For x0: ((-1.0)^2*1 + (0.0)^2*4 + (1.0)^2*1 + (-1.0)^2*2 + (1.0)^2*2) / 10 = 0.6
        // For x1: ((-1.0)^2*1 + (0.0)^2*4 + (1.0)^2*1 + (1.0)^2*2 + (-1.0)^2*2) / 10 = 0.6
        // For x2: ((-1.0)^2*1 + (0.0)^2*4 + (1.0)^2*1 + (0.0)^2*2 + (0.0)^2*2) / 10 = 0.2
        // For x3: ((-1.0)^2*1 + (0.0)^2*4 + (1.0)^2*1 + (-1.0)^2*2 + (1.0)^2*2) / 10 = 0.6
        let expected2 = { x0 = 0.6; x1 = 0.6; x2 = 0.2; x3 = 0.6 }
        let diff2 = (moment2 - expected2)
        diff2.total().Should().BeApproximately(0.0, 1e-10, "because the 2nd moment should be the weighted average of squared coordinates") |> ignore

    [<Fact>]
    member _.``5D SparseArray moment calculations should be correct``() =
        // Arrange
        // Create domain in [-1, 1] interval
        let domainRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 10
        let domain = Domain5D.create(intervals, domainRange)

        // Create a sparse array with points - creating a simpler distribution for 5D
        let sparseValues =
            [|
                { x = { i0 = 0; i1 = 0; i2 = 0; i3 = 0; i4 = 0 }; value = 1L }                   // all -1.0
                { x = { i0 = 5; i1 = 5; i2 = 5; i3 = 5; i4 = 5 }; value = 8L }                   // all 0.0
                { x = { i0 = 10; i1 = 10; i2 = 10; i3 = 10; i4 = 10 }; value = 1L }              // all 1.0
            |]

        let sparseArray = SparseArray.create sparseValues
        let conversionParameters = conversionParameters5D domain

        // Act
        let moment0 = sparseArray.moment conversionParameters 0
        let moment1 = sparseArray.moment conversionParameters 1
        let moment2 = sparseArray.moment conversionParameters 2

        // Assert
        output.WriteLine($"Domain 5D with range: [{domain.d0.domainRange.minValue}, {domain.d0.domainRange.maxValue}]")
        output.WriteLine($"Moment 0: {moment0}")
        output.WriteLine($"Moment 1: {moment1}")
        output.WriteLine($"Moment 2: {moment2}")

        // Total weight = 10

        // Verify calculations
        // Moment 0 is always 1 (the sum of probabilities)
        let diff0 = (moment0 - Coord5D.One)
        diff0.total().Should().BeApproximately(0.0, 1e-10, "because the 0th moment should always be 1") |> ignore

        // Moment 1 calculations
        // For all dimensions: symmetrically distributed, expect 0.0
        let expected1 = { x0 = 0.0; x1 = 0.0; x2 = 0.0; x3 = 0.0; x4 = 0.0 }
        let diff1 = (moment1 - expected1)
        diff1.total().Should().BeApproximately(0.0, 1e-10, "because the 1st moment should be the weighted average of coordinates") |> ignore

        // Moment 2 calculations for each dimension
        // For all dimensions: ((-1.0)^2*1 + (0.0)^2*8 + (1.0)^2*1) / 10 = 0.2
        let expected2 = { x0 = 0.2; x1 = 0.2; x2 = 0.2; x3 = 0.2; x4 = 0.2 }
        let diff2 = (moment2 - expected2)
        diff2.total().Should().BeApproximately(0.0, 1e-10, "because the 2nd moment should be the weighted average of squared coordinates") |> ignore

    [<Fact>]
    member _.``6D SparseArray moment calculations should be correct``() =
        // Arrange
        // Create domain in [-1, 1] interval
        let domainRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 10
        let domain = Domain6D.create(intervals, domainRange)

        // Create a sparse array with points - creating a simpler distribution for 6D
        let sparseValues =
            [|
                { x = { i0 = 0; i1 = 0; i2 = 0; i3 = 0; i4 = 0; i5 = 0 }; value = 1L }                   // all -1.0
                { x = { i0 = 5; i1 = 5; i2 = 5; i3 = 5; i4 = 5; i5 = 5 }; value = 8L }                   // all 0.0
                { x = { i0 = 10; i1 = 10; i2 = 10; i3 = 10; i4 = 10; i5 = 10 }; value = 1L }             // all 1.0
            |]

        let sparseArray = SparseArray.create sparseValues
        let conversionParameters = conversionParameters6D domain

        // Act
        let moment0 = sparseArray.moment conversionParameters 0
        let moment1 = sparseArray.moment conversionParameters 1
        let moment2 = sparseArray.moment conversionParameters 2

        // Assert
        output.WriteLine($"Domain 6D with range: [{domain.d0.domainRange.minValue}, {domain.d0.domainRange.maxValue}]")
        output.WriteLine($"Moment 0: {moment0}")
        output.WriteLine($"Moment 1: {moment1}")
        output.WriteLine($"Moment 2: {moment2}")

        // Total weight = 10

        // Verify calculations
        // Moment 0 is always 1 (the sum of probabilities)
        let diff0 = (moment0 - Coord6D.One)
        diff0.total().Should().BeApproximately(0.0, 1e-10, "because the 0th moment should always be 1") |> ignore

        // Moment 1 calculations
        // For all dimensions: symmetrically distributed, expect 0.0
        let expected1 = { x0 = 0.0; x1 = 0.0; x2 = 0.0; x3 = 0.0; x4 = 0.0; x5 = 0.0 }
        let diff1 = (moment1 - expected1)
        diff1.total().Should().BeApproximately(0.0, 1e-10, "because the 1st moment should be the weighted average of coordinates") |> ignore

        // Moment 2 calculations for each dimension
        // For all dimensions: ((-1.0)^2*1 + (0.0)^2*8 + (1.0)^2*1) / 10 = 0.2
        let expected2 = { x0 = 0.2; x1 = 0.2; x2 = 0.2; x3 = 0.2; x4 = 0.2; x5 = 0.2 }
        let diff2 = (moment2 - expected2)
        diff2.total().Should().BeApproximately(0.0, 1e-10, "because the 2nd moment should be the weighted average of squared coordinates") |> ignore

    [<Fact>]
    member _.``7D SparseArray moment calculations should be correct``() =
        // Arrange
        // Create domain in [-1, 1] interval
        let domainRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 10
        let domain = Domain7D.create(intervals, domainRange)

        // Create a sparse array with points - creating a simpler distribution for 7D
        let sparseValues =
            [|
                { x = { i0 = 0; i1 = 0; i2 = 0; i3 = 0; i4 = 0; i5 = 0; i6 = 0 }; value = 1L }                   // all -1.0
                { x = { i0 = 5; i1 = 5; i2 = 5; i3 = 5; i4 = 5; i5 = 5; i6 = 5 }; value = 8L }                   // all 0.0
                { x = { i0 = 10; i1 = 10; i2 = 10; i3 = 10; i4 = 10; i5 = 10; i6 = 10 }; value = 1L }            // all 1.0
            |]

        let sparseArray = SparseArray.create sparseValues
        let conversionParameters = conversionParameters7D domain

        // Act
        let moment0 = sparseArray.moment conversionParameters 0
        let moment1 = sparseArray.moment conversionParameters 1
        let moment2 = sparseArray.moment conversionParameters 2

        // Assert
        output.WriteLine($"Domain 7D with range: [{domain.d0.domainRange.minValue}, {domain.d0.domainRange.maxValue}]")
        output.WriteLine($"Moment 0: {moment0}")
        output.WriteLine($"Moment 1: {moment1}")
        output.WriteLine($"Moment 2: {moment2}")

        // Total weight = 10

        // Verify calculations
        // Moment 0 is always 1 (the sum of probabilities)
        let diff0 = (moment0 - Coord7D.One)
        diff0.total().Should().BeApproximately(0.0, 1e-10, "because the 0th moment should always be 1") |> ignore

        // Moment 1 calculations
        // For all dimensions: symmetrically distributed, expect 0.0
        let expected1 = { x0 = 0.0; x1 = 0.0; x2 = 0.0; x3 = 0.0; x4 = 0.0; x5 = 0.0; x6 = 0.0 }
        let diff1 = (moment1 - expected1)
        diff1.total().Should().BeApproximately(0.0, 1e-10, "because the 1st moment should be the weighted average of coordinates") |> ignore

        // Moment 2 calculations for each dimension
        // For all dimensions: ((-1.0)^2*1 + (0.0)^2*8 + (1.0)^2*1) / 10 = 0.2
        let expected2 = { x0 = 0.2; x1 = 0.2; x2 = 0.2; x3 = 0.2; x4 = 0.2; x5 = 0.2; x6 = 0.2 }
        let diff2 = (moment2 - expected2)
        diff2.total().Should().BeApproximately(0.0, 1e-10, "because the 2nd moment should be the weighted average of squared coordinates") |> ignore

    [<Fact>]
    member _.``8D SparseArray moment calculations should be correct``() =
        // Arrange
        // Create domain in [-1, 1] interval
        let domainRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 10
        let domain = Domain8D.create(intervals, domainRange)

        // Create a sparse array with points - creating a simpler distribution for 8D
        let sparseValues =
            [|
                { x = { i0 = 0; i1 = 0; i2 = 0; i3 = 0; i4 = 0; i5 = 0; i6 = 0; i7 = 0 }; value = 1L }                   // all -1.0
                { x = { i0 = 5; i1 = 5; i2 = 5; i3 = 5; i4 = 5; i5 = 5; i6 = 5; i7 = 5 }; value = 8L }                   // all 0.0
                { x = { i0 = 10; i1 = 10; i2 = 10; i3 = 10; i4 = 10; i5 = 10; i6 = 10; i7 = 10 }; value = 1L }           // all 1.0
            |]

        let sparseArray = SparseArray.create sparseValues
        let conversionParameters = conversionParameters8D domain

        // Act
        let moment0 = sparseArray.moment conversionParameters 0
        let moment1 = sparseArray.moment conversionParameters 1
        let moment2 = sparseArray.moment conversionParameters 2

        // Assert
        output.WriteLine($"Domain 8D with range: [{domain.d0.domainRange.minValue}, {domain.d0.domainRange.maxValue}]")
        output.WriteLine($"Moment 0: {moment0}")
        output.WriteLine($"Moment 1: {moment1}")
        output.WriteLine($"Moment 2: {moment2}")

        // Total weight = 10

        // Verify calculations
        // Moment 0 is always 1 (the sum of probabilities)
        let diff0 = (moment0 - Coord8D.One)
        diff0.total().Should().BeApproximately(0.0, 1e-10, "because the 0th moment should always be 1") |> ignore

        // Moment 1 calculations
        // For all dimensions: symmetrically distributed, expect 0.0
        let expected1 = { x0 = 0.0; x1 = 0.0; x2 = 0.0; x3 = 0.0; x4 = 0.0; x5 = 0.0; x6 = 0.0; x7 = 0.0 }
        let diff1 = (moment1 - expected1)
        diff1.total().Should().BeApproximately(0.0, 1e-10, "because the 1st moment should be the weighted average of coordinates") |> ignore

        // Moment 2 calculations for each dimension
        // For all dimensions: ((-1.0)^2*1 + (0.0)^2*8 + (1.0)^2*1) / 10 = 0.2
        let expected2 = { x0 = 0.2; x1 = 0.2; x2 = 0.2; x3 = 0.2; x4 = 0.2; x5 = 0.2; x6 = 0.2; x7 = 0.2 }
        let diff2 = (moment2 - expected2)
        diff2.total().Should().BeApproximately(0.0, 1e-10, "because the 2nd moment should be the weighted average of squared coordinates") |> ignore

    [<Fact>]
    member _.``8D SparseArray moment calculations should be correct with mixed coordinates``() =
        // Arrange
        // Create domain in [-1, 1] interval
        let domainRange = { minValue = -1.0; maxValue = 1.0 }
        let intervals = DomainIntervals 20  // Finer grid for more variety
        let domain = Domain8D.create(intervals, domainRange)

        // Create a sparse array with points with mixed, non-symmetrical coordinates
        let sparseValues =
            [|
                { x = { i0 = 4; i1 = 8; i2 = 12; i3 = 16; i4 = 6; i5 = 10; i6 = 14; i7 = 18 }; value = 2L }
                { x = { i0 = 10; i1 = 10; i2 = 10; i3 = 10; i4 = 10; i5 = 10; i6 = 10; i7 = 10 }; value = 5L }
                { x = { i0 = 15; i1 = 5; i2 = 3; i3 = 12; i4 = 18; i5 = 7; i6 = 9; i7 = 11 }; value = 3L }
            |]

        let sparseArray = SparseArray.create sparseValues
        let conversionParameters = conversionParameters8D domain

        // The projector function to map point to coordinate
        let projector (p: Point8D) = p.toCoord domain

        // Helper function to manually calculate expected moments
        let calculateMoments () =
            // Extract coordinates and weights
            let pointsWithWeights = sparseValues |> Array.map (fun sv ->
                let coord = projector sv.x
                let weight = double sv.value
                (coord, weight))

            // Total weight
            let totalWeight = pointsWithWeights |> Array.sumBy snd

            // Calculate first moment (mean)
            let moment1 =
                pointsWithWeights
                |> Array.fold (fun acc (coord, weight) ->
                    let weightedCoord = coord *. weight
                    acc + weightedCoord) Coord8D.Zero
                |> fun sum -> sum /. totalWeight

            // Calculate second moment (mean of squares)
            let moment2 =
                pointsWithWeights
                |> Array.fold (fun acc (coord, weight) ->
                    let squaredCoord = {
                        x0 = coord.x0 * coord.x0
                        x1 = coord.x1 * coord.x1
                        x2 = coord.x2 * coord.x2
                        x3 = coord.x3 * coord.x3
                        x4 = coord.x4 * coord.x4
                        x5 = coord.x5 * coord.x5
                        x6 = coord.x6 * coord.x6
                        x7 = coord.x7 * coord.x7
                    }
                    acc + (squaredCoord *. weight)) Coord8D.Zero
                |> fun sum -> sum /. totalWeight

            (moment1, moment2)

        // Act
        let moment0 = sparseArray.moment conversionParameters 0
        let moment1 = sparseArray.moment conversionParameters 1
        let moment2 = sparseArray.moment conversionParameters 2

        // Calculate expected values manually
        let (expectedMoment1, expectedMoment2) = calculateMoments()

        // Assert
        output.WriteLine($"Domain 8D with mixed coordinates")
        output.WriteLine($"Moment 0: {moment0}")
        output.WriteLine($"Moment 1: {moment1}")
        output.WriteLine($"Expected Moment 1: {expectedMoment1}")
        output.WriteLine($"Moment 2: {moment2}")
        output.WriteLine($"Expected Moment 2: {expectedMoment2}")

        // Verify calculations
        // Moment 0 is always 1 (the sum of probabilities)
        let diff0 = (moment0 - Coord8D.One)
        diff0.total().Should().BeApproximately(0.0, 1e-10, "because the 0th moment should always be 1") |> ignore

        // Moment 1 - compare with calculated expected value
        let diff1 = (moment1 - expectedMoment1)
        diff1.total().Should().BeApproximately(0.0, 1e-10, "because the 1st moment should match the manually calculated value") |> ignore

        // Moment 2 - compare with calculated expected value
        let diff2 = (moment2 - expectedMoment2)
        diff2.total().Should().BeApproximately(0.0, 1e-10, "because the 2nd moment should match the manually calculated value") |> ignore
