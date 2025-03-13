namespace Softellect.Tests.MathTests

open System
open System.Diagnostics
open FluentAssertions
open FluentAssertions.Execution
open Xunit
open Xunit.Abstractions
open Softellect.Math.Primitives
open Softellect.Math.Sparse
open Softellect.Math.Tridiagonal

module MultidimensionalRecordTests =
    /// Helper functions for creating multi-dimensional test data
    module Helpers =
        let create2DTridiagonalMatrix = createTridiagonalMatrix2D
        let create3DTridiagonalMatrix = createTridiagonalMatrix3D
        let create4DTridiagonalMatrix = createTridiagonalMatrix4D
        let create5DTridiagonalMatrix = createTridiagonalMatrix5D
        let create6DTridiagonalMatrix = createTridiagonalMatrix6D
        let create7DTridiagonalMatrix = createTridiagonalMatrix7D
        let create8DTridiagonalMatrix = createTridiagonalMatrix8D


        /// Map a dimension index to normalized range [-1, 1]
        let normalizeIndex (d: int) (idx: int) =
            (float idx) * 2.0 / (float (d - 1)) - 1.0

        /// Check if a point is near a hypersphere with radius r
        let isNearHypersphere (radius: float) (epsilon: float) (coords: float array) =
            let sumSquares =
                coords
                |> Array.sumBy (fun x -> x * x)

            let dist = Math.Sqrt(sumSquares)
            Math.Abs(dist - radius) <= epsilon

        /// Calculate the total size of a hypercube with dimension d and k dimensions
        let totalSize (d: int) (k: int) =
            pown (int64 d) k

        /// Calculate the exact number of non-zero elements in a k-dimensional tridiagonal matrix
        let calculateTridiagonalNonZeros (d: int) (k: int) : int64 =
            // Special case: if k = 1, it's just a standard tridiagonal matrix
            if k = 1 then
                // d diagonal elements + 2(d-1) off-diagonal elements
                (int64 d) + 2L * (int64 (d - 1))
            else
                let d64 = int64 d
                let k64 = int64 k

                // For a k-dimensional tridiagonal matrix, each point has:
                // - 1 diagonal element (self-connection)
                // - Up to 2k off-diagonal elements (±1 in each dimension)

                // The challenge is that boundary points have fewer connections

                // We can count the actual matrix elements by considering:
                // 1. Each point contributes one diagonal element: d^k
                // 2. Each interior connection contributes one element
                //    (not two, because we're counting matrix elements, not edges)

                // Total number of points in the grid
                let numPoints = pown d64 k

                // The number of actual connections in the matrix:
                // - Diagonal connections: d^k
                // - Off-diagonal connections:
                //   Each dimension contributes (d-1)*d^(k-1) connections
                //   These are one-way connections, so each is counted once

                let diagonalElements = numPoints

                // Calculate off-diagonal elements from the matrix structure
                let offDiagonalElements =
                    let oneWayConnectionsPerDim = (d64 - 1L) * pown d64 (k-1)
                    2L * k64 * oneWayConnectionsPerDim

                // Total elements
                diagonalElements + offDiagonalElements


        // 2D Matrix and Vector Functions

        // /// Create a 2D tridiagonal sparse matrix
        // let create2DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point2D, float> =
        //     // Parameter validation
        //     if a <= 0.0 || a >= 1.0 then
        //         failwith $"Parameter a must be in range (0, 1)"
        //
        //     // Calculate b based on the constraint a + 2b = 1
        //     let b = (1.0 - a) / 2.0
        //
        //     // Create the matrix as functions (no full instantiation)
        //     let x_y point =
        //         let values = ResizeArray<SparseValue<Point2D, float>>()
        //
        //         // Diagonal element (self-connection)
        //         values.Add({ x = point; value = a })
        //
        //         // Off-diagonal in x0 direction
        //         if point.x0 > 0 then
        //             values.Add({ x = { x0 = point.x0 - 1; x1 = point.x1 }; value = b })
        //
        //         if point.x0 < d - 1 then
        //             values.Add({ x = { x0 = point.x0 + 1; x1 = point.x1 }; value = b })
        //
        //         // Off-diagonal in x1 direction
        //         if point.x1 > 0 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1 - 1 }; value = b })
        //
        //         if point.x1 < d - 1 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1 + 1 }; value = b })
        //
        //         SparseArray.create (values.ToArray())
        //
        //     { x_y = x_y; y_x = x_y }

        /// Create a 2D hypersphere vector
        let create2DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point2D, float> =
            let values = ResizeArray<SparseValue<Point2D, float>>()

            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    let x = normalizeIndex d i
                    let y = normalizeIndex d j

                    // Check if point is on the hypersphere (circle in 2D)
                    if isNearHypersphere radius epsilon [|x; y|] then
                        values.Add({ x = { i0 = i; i1 = j }; value = 1.0 })

            SparseArray.create (values.ToArray())

        // 3D Matrix and Vector Functions

        // /// Create a 3D tridiagonal sparse matrix
        // let create3DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point3D, float> =
        //     // Parameter validation
        //     if a <= 0.0 || a >= 1.0 then
        //         failwith $"Parameter a must be in range (0, 1)"
        //
        //     // Calculate b based on the constraint a + 2b = 1
        //     let b = (1.0 - a) / 2.0
        //
        //     // Create the matrix as functions (no full instantiation)
        //     let x_y point =
        //         let values = ResizeArray<SparseValue<Point3D, float>>()
        //
        //         // Diagonal element (self-connection)
        //         values.Add({ x = point; value = a })
        //
        //         // Off-diagonal in x0 direction
        //         if point.x0 > 0 then
        //             values.Add({ x = { x0 = point.x0 - 1; x1 = point.x1; x2 = point.x2 }; value = b })
        //
        //         if point.x0 < d - 1 then
        //             values.Add({ x = { x0 = point.x0 + 1; x1 = point.x1; x2 = point.x2 }; value = b })
        //
        //         // Off-diagonal in x1 direction
        //         if point.x1 > 0 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1 - 1; x2 = point.x2 }; value = b })
        //
        //         if point.x1 < d - 1 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1 + 1; x2 = point.x2 }; value = b })
        //
        //         // Off-diagonal in x2 direction
        //         if point.x2 > 0 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2 - 1 }; value = b })
        //
        //         if point.x2 < d - 1 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2 + 1 }; value = b })
        //
        //         SparseArray.create (values.ToArray())
        //
        //     { x_y = x_y; y_x = x_y }

        /// Create a 3D hypersphere vector
        let create3DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point3D, float> =
            let values = ResizeArray<SparseValue<Point3D, float>>()

            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    for k in 0..(d-1) do
                        let x = normalizeIndex d i
                        let y = normalizeIndex d j
                        let z = normalizeIndex d k

                        // Check if point is on the hypersphere (sphere in 3D)
                        if isNearHypersphere radius epsilon [|x; y; z|] then
                            values.Add({ x = { i0 = i; i1 = j; i2 = k }; value = 1.0 })

            SparseArray.create (values.ToArray())

        // 4D Matrix and Vector Functions

        // /// Create a 4D tridiagonal sparse matrix
        // let create4DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point4D, float> =
        //     // Parameter validation
        //     if a <= 0.0 || a >= 1.0 then
        //         failwith $"Parameter a must be in range (0, 1)"
        //
        //     // Calculate b based on the constraint a + 2b = 1
        //     let b = (1.0 - a) / 2.0
        //
        //     // Create the matrix as functions (no full instantiation)
        //     let x_y point =
        //         let values = ResizeArray<SparseValue<Point4D, float>>()
        //
        //         // Diagonal element (self-connection)
        //         values.Add({ x = point; value = a })
        //
        //         // Off-diagonal in x0 direction
        //         if point.x0 > 0 then
        //             values.Add({ x = { x0 = point.x0 - 1; x1 = point.x1; x2 = point.x2; x3 = point.x3 }; value = b })
        //
        //         if point.x0 < d - 1 then
        //             values.Add({ x = { x0 = point.x0 + 1; x1 = point.x1; x2 = point.x2; x3 = point.x3 }; value = b })
        //
        //         // Off-diagonal in x1 direction
        //         if point.x1 > 0 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1 - 1; x2 = point.x2; x3 = point.x3 }; value = b })
        //
        //         if point.x1 < d - 1 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1 + 1; x2 = point.x2; x3 = point.x3 }; value = b })
        //
        //         // Off-diagonal in x2 direction
        //         if point.x2 > 0 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2 - 1; x3 = point.x3 }; value = b })
        //
        //         if point.x2 < d - 1 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2 + 1; x3 = point.x3 }; value = b })
        //
        //         // Off-diagonal in x3 direction
        //         if point.x3 > 0 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2; x3 = point.x3 - 1 }; value = b })
        //
        //         if point.x3 < d - 1 then
        //             values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2; x3 = point.x3 + 1 }; value = b })
        //
        //         SparseArray.create (values.ToArray())
        //
        //     { x_y = x_y; y_x = x_y }

        /// Create a 4D hypersphere vector
        let create4DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point4D, float> =
            let values = ResizeArray<SparseValue<Point4D, float>>()

            // For high dimensions, we need to be more selective about which points to include
            let pointsToCheck = min d (max 100 (1000 / 4)) // Reduce points for higher dimensions

            // We'll use a regular sample to explore the space
            let stepSize = max 1 (d / pointsToCheck)

            for i in 0 .. stepSize .. (d-1) do
                for j in 0 .. stepSize .. (d-1) do
                    for k in 0 .. stepSize .. (d-1) do
                        for l in 0 .. stepSize .. (d-1) do
                            let x0 = normalizeIndex d i
                            let x1 = normalizeIndex d j
                            let x2 = normalizeIndex d k
                            let x3 = normalizeIndex d l

                            // Check if point is on the hypersphere
                            if isNearHypersphere radius epsilon [|x0; x1; x2; x3|] then
                                values.Add({ x = { i0 = i; i1 = j; i2 = k; i3 = l }; value = 1.0 })

            SparseArray.create (values.ToArray())

        // 5D Matrix and Vector Functions

        // /// Create a 5D tridiagonal sparse matrix
        // let create5DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point5D, float> =
        //     // Parameter validation
        //     if a <= 0.0 || a >= 1.0 then
        //         failwith $"Parameter a must be in range (0, 1)"
        //
        //     // Calculate b based on the constraint a + 2b = 1
        //     let b = (1.0 - a) / 2.0
        //
        //     // Create the matrix as functions (no full instantiation)
        //     let x_y point =
        //         let values = ResizeArray<SparseValue<Point5D, float>>()
        //
        //         // Diagonal element (self-connection)
        //         values.Add({ x = point; value = a })
        //
        //         // Off-diagonal in x0 direction
        //         if point.x0 > 0 then
        //             values.Add({ x = { point with x0 = point.x0 - 1 }; value = b })
        //
        //         if point.x0 < d - 1 then
        //             values.Add({ x = { point with x0 = point.x0 + 1 }; value = b })
        //
        //         // Off-diagonal in x1 direction
        //         if point.x1 > 0 then
        //             values.Add({ x = { point with x1 = point.x1 - 1 }; value = b })
        //
        //         if point.x1 < d - 1 then
        //             values.Add({ x = { point with x1 = point.x1 + 1 }; value = b })
        //
        //         // Off-diagonal in x2 direction
        //         if point.x2 > 0 then
        //             values.Add({ x = { point with x2 = point.x2 - 1 }; value = b })
        //
        //         if point.x2 < d - 1 then
        //             values.Add({ x = { point with x2 = point.x2 + 1 }; value = b })
        //
        //         // Off-diagonal in x3 direction
        //         if point.x3 > 0 then
        //             values.Add({ x = { point with x3 = point.x3 - 1 }; value = b })
        //
        //         if point.x3 < d - 1 then
        //             values.Add({ x = { point with x3 = point.x3 + 1 }; value = b })
        //
        //         // Off-diagonal in x4 direction
        //         if point.x4 > 0 then
        //             values.Add({ x = { point with x4 = point.x4 - 1 }; value = b })
        //
        //         if point.x4 < d - 1 then
        //             values.Add({ x = { point with x4 = point.x4 + 1 }; value = b })
        //
        //         SparseArray.create (values.ToArray())
        //
        //     { x_y = x_y; y_x = x_y }

        /// Create a 5D hypersphere vector
        let create5DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point5D, float> =
            let values = ResizeArray<SparseValue<Point5D, float>>()

            // For high dimensions, we need to be more selective about which points to include
            let pointsToCheck = min d (max 50 (1000 / 5)) // Reduce points for higher dimensions

            // We'll use a regular sample to explore the space
            let stepSize = max 1 (d / pointsToCheck)

            for i in 0 .. stepSize .. (d-1) do
                for j in 0 .. stepSize .. (d-1) do
                    for k in 0 .. stepSize .. (d-1) do
                        for l in 0 .. stepSize .. (d-1) do
                            for m in 0 .. stepSize .. (d-1) do
                                let x0 = normalizeIndex d i
                                let x1 = normalizeIndex d j
                                let x2 = normalizeIndex d k
                                let x3 = normalizeIndex d l
                                let x4 = normalizeIndex d m

                                // Check if point is on the hypersphere
                                if isNearHypersphere radius epsilon [|x0; x1; x2; x3; x4|] then
                                    values.Add({ x = { x0 = i; x1 = j; x2 = k; x3 = l; x4 = m }; value = 1.0 })

            SparseArray.create (values.ToArray())

        // 6D Matrix and Vector Functions

        // /// Create a 6D tridiagonal sparse matrix
        // let create6DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point6D, float> =
        //     // Parameter validation
        //     if a <= 0.0 || a >= 1.0 then
        //         failwith $"Parameter a must be in range (0, 1)"
        //
        //     // Calculate b based on the constraint a + 2b = 1
        //     let b = (1.0 - a) / 2.0
        //
        //     // Create the matrix as functions (no full instantiation)
        //     let x_y point =
        //         let values = ResizeArray<SparseValue<Point6D, float>>()
        //
        //         // Diagonal element (self-connection)
        //         values.Add({ x = point; value = a })
        //
        //         // Off-diagonal in x0 direction
        //         if point.x0 > 0 then
        //             values.Add({ x = { point with x0 = point.x0 - 1 }; value = b })
        //
        //         if point.x0 < d - 1 then
        //             values.Add({ x = { point with x0 = point.x0 + 1 }; value = b })
        //
        //         // Off-diagonal in x1 direction
        //         if point.x1 > 0 then
        //             values.Add({ x = { point with x1 = point.x1 - 1 }; value = b })
        //
        //         if point.x1 < d - 1 then
        //             values.Add({ x = { point with x1 = point.x1 + 1 }; value = b })
        //
        //         // Off-diagonal in x2 direction
        //         if point.x2 > 0 then
        //             values.Add({ x = { point with x2 = point.x2 - 1 }; value = b })
        //
        //         if point.x2 < d - 1 then
        //             values.Add({ x = { point with x2 = point.x2 + 1 }; value = b })
        //
        //         // Off-diagonal in x3 direction
        //         if point.x3 > 0 then
        //             values.Add({ x = { point with x3 = point.x3 - 1 }; value = b })
        //
        //         if point.x3 < d - 1 then
        //             values.Add({ x = { point with x3 = point.x3 + 1 }; value = b })
        //
        //         // Off-diagonal in x4 direction
        //         if point.x4 > 0 then
        //             values.Add({ x = { point with x4 = point.x4 - 1 }; value = b })
        //
        //         if point.x4 < d - 1 then
        //             values.Add({ x = { point with x4 = point.x4 + 1 }; value = b })
        //
        //         // Off-diagonal in x5 direction
        //         if point.x5 > 0 then
        //             values.Add({ x = { point with x5 = point.x5 - 1 }; value = b })
        //
        //         if point.x5 < d - 1 then
        //             values.Add({ x = { point with x5 = point.x5 + 1 }; value = b })
        //
        //         SparseArray.create (values.ToArray())
        //
        //     { x_y = x_y; y_x = x_y }

        /// Create a 6D hypersphere vector
        let create6DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point6D, float> =
            let values = ResizeArray<SparseValue<Point6D, float>>()

            // For high dimensions, we need to be more selective about which points to include
            let pointsToCheck = min d (max 25 (1000 / 6)) // Reduce points for higher dimensions

            // We'll use a regular sample to explore the space
            let stepSize = max 1 (d / pointsToCheck)

            for i in 0 .. stepSize .. (d-1) do
                for j in 0 .. stepSize .. (d-1) do
                    for k in 0 .. stepSize .. (d-1) do
                        for l in 0 .. stepSize .. (d-1) do
                            for m in 0 .. stepSize .. (d-1) do
                                for n in 0 .. stepSize .. (d-1) do
                                    let x0 = normalizeIndex d i
                                    let x1 = normalizeIndex d j
                                    let x2 = normalizeIndex d k
                                    let x3 = normalizeIndex d l
                                    let x4 = normalizeIndex d m
                                    let x5 = normalizeIndex d n

                                    // Check if point is on the hypersphere
                                    if isNearHypersphere radius epsilon [|x0; x1; x2; x3; x4; x5|] then
                                        values.Add({ x = { x0 = i; x1 = j; x2 = k; x3 = l; x4 = m; x5 = n }; value = 1.0 })

            SparseArray.create (values.ToArray())

        // 7D Matrix and Vector Functions

        // /// Create a 7D tridiagonal sparse matrix
        // let create7DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point7D, float> =
        //     // Parameter validation
        //     if a <= 0.0 || a >= 1.0 then
        //         failwith $"Parameter a must be in range (0, 1)"
        //
        //     // Calculate b based on the constraint a + 2b = 1
        //     let b = (1.0 - a) / 2.0
        //
        //     // Create the matrix as functions (no full instantiation)
        //     let x_y point =
        //         let values = ResizeArray<SparseValue<Point7D, float>>()
        //
        //         // Diagonal element (self-connection)
        //         values.Add({ x = point; value = a })
        //
        //         // Off-diagonal in x0 direction
        //         if point.x0 > 0 then
        //             values.Add({ x = { point with x0 = point.x0 - 1 }; value = b })
        //
        //         if point.x0 < d - 1 then
        //             values.Add({ x = { point with x0 = point.x0 + 1 }; value = b })
        //
        //         // Off-diagonal in x1 direction
        //         if point.x1 > 0 then
        //             values.Add({ x = { point with x1 = point.x1 - 1 }; value = b })
        //
        //         if point.x1 < d - 1 then
        //             values.Add({ x = { point with x1 = point.x1 + 1 }; value = b })
        //
        //         // Off-diagonal in x2 direction
        //         if point.x2 > 0 then
        //             values.Add({ x = { point with x2 = point.x2 - 1 }; value = b })
        //
        //         if point.x2 < d - 1 then
        //             values.Add({ x = { point with x2 = point.x2 + 1 }; value = b })
        //
        //         // Off-diagonal in x3 direction
        //         if point.x3 > 0 then
        //             values.Add({ x = { point with x3 = point.x3 - 1 }; value = b })
        //
        //         if point.x3 < d - 1 then
        //             values.Add({ x = { point with x3 = point.x3 + 1 }; value = b })
        //
        //         // Off-diagonal in x4 direction
        //         if point.x4 > 0 then
        //             values.Add({ x = { point with x4 = point.x4 - 1 }; value = b })
        //
        //         if point.x4 < d - 1 then
        //             values.Add({ x = { point with x4 = point.x4 + 1 }; value = b })
        //
        //         // Off-diagonal in x5 direction
        //         if point.x5 > 0 then
        //             values.Add({ x = { point with x5 = point.x5 - 1 }; value = b })
        //
        //         if point.x5 < d - 1 then
        //             values.Add({ x = { point with x5 = point.x5 + 1 }; value = b })
        //
        //         // Off-diagonal in x6 direction
        //         if point.x6 > 0 then
        //             values.Add({ x = { point with x6 = point.x6 - 1 }; value = b })
        //
        //         if point.x6 < d - 1 then
        //             values.Add({ x = { point with x6 = point.x6 + 1 }; value = b })
        //
        //         SparseArray.create (values.ToArray())
        //
        //     { x_y = x_y; y_x = x_y }

        /// Create a 7D hypersphere vector
        let create7DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point7D, float> =
            let values = ResizeArray<SparseValue<Point7D, float>>()

            // For high dimensions, we need to be even more selective
            let pointsToCheck = min d (max 10 (1000 / 7))

            // We'll use a regular sample to explore the space
            let stepSize = max 1 (d / pointsToCheck)

            // Use a recursive approach for high dimensions
            let rec generatePoints (coords: int list) (dimension: int) =
                if dimension = 7 then
                    // Convert coordinates to normalized space
                    let normalizedCoords =
                        coords
                        |> List.rev
                        |> List.map (fun idx -> normalizeIndex d idx)
                        |> List.toArray

                    // Check if point is on the hypersphere
                    if isNearHypersphere radius epsilon normalizedCoords then
                        let point =
                            let coords = List.rev coords
                            {
                                x0 = List.item 0 coords
                                x1 = List.item 1 coords
                                x2 = List.item 2 coords
                                x3 = List.item 3 coords
                                x4 = List.item 4 coords
                                x5 = List.item 5 coords
                                x6 = List.item 6 coords
                            }
                        values.Add({ x = point; value = 1.0 })
                else
                    for i in 0 .. stepSize .. (d-1) do
                        generatePoints (i :: coords) (dimension + 1)

            generatePoints [] 0

            SparseArray.create (values.ToArray())

        // 8D Matrix and Vector Functions

        // /// Create a 8D tridiagonal sparse matrix
        // let create8DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point8D, float> =
        //     // Parameter validation
        //     if a <= 0.0 || a >= 1.0 then
        //         failwith $"Parameter a must be in range (0, 1)"
        //
        //     // Calculate b based on the constraint a + 2b = 1
        //     let b = (1.0 - a) / 2.0
        //
        //     // Create the matrix as functions (no full instantiation)
        //     let x_y point =
        //         let values = ResizeArray<SparseValue<Point8D, float>>()
        //
        //         // Diagonal element (self-connection)
        //         values.Add({ x = point; value = a })
        //
        //         // Off-diagonal in x0 direction
        //         if point.x0 > 0 then
        //             values.Add({ x = { point with x0 = point.x0 - 1 }; value = b })
        //
        //         if point.x0 < d - 1 then
        //             values.Add({ x = { point with x0 = point.x0 + 1 }; value = b })
        //
        //         // Off-diagonal in x1 direction
        //         if point.x1 > 0 then
        //             values.Add({ x = { point with x1 = point.x1 - 1 }; value = b })
        //
        //         if point.x1 < d - 1 then
        //             values.Add({ x = { point with x1 = point.x1 + 1 }; value = b })
        //
        //         // Off-diagonal in x2 direction
        //         if point.x2 > 0 then
        //             values.Add({ x = { point with x2 = point.x2 - 1 }; value = b })
        //
        //         if point.x2 < d - 1 then
        //             values.Add({ x = { point with x2 = point.x2 + 1 }; value = b })
        //
        //         // Off-diagonal in x3 direction
        //         if point.x3 > 0 then
        //             values.Add({ x = { point with x3 = point.x3 - 1 }; value = b })
        //
        //         if point.x3 < d - 1 then
        //             values.Add({ x = { point with x3 = point.x3 + 1 }; value = b })
        //
        //         // Off-diagonal in x4 direction
        //         if point.x4 > 0 then
        //             values.Add({ x = { point with x4 = point.x4 - 1 }; value = b })
        //
        //         if point.x4 < d - 1 then
        //             values.Add({ x = { point with x4 = point.x4 + 1 }; value = b })
        //
        //         // Off-diagonal in x5 direction
        //         if point.x5 > 0 then
        //             values.Add({ x = { point with x5 = point.x5 - 1 }; value = b })
        //
        //         if point.x5 < d - 1 then
        //             values.Add({ x = { point with x5 = point.x5 + 1 }; value = b })
        //
        //         // Off-diagonal in x6 direction
        //         if point.x6 > 0 then
        //             values.Add({ x = { point with x6 = point.x6 - 1 }; value = b })
        //
        //         if point.x6 < d - 1 then
        //             values.Add({ x = { point with x6 = point.x6 + 1 }; value = b })
        //
        //         // Off-diagonal in x7 direction
        //         if point.x7 > 0 then
        //             values.Add({ x = { point with x7 = point.x7 - 1 }; value = b })
        //
        //         if point.x7 < d - 1 then
        //             values.Add({ x = { point with x7 = point.x7 + 1 }; value = b })
        //
        //         SparseArray.create (values.ToArray())
        //
        //     { x_y = x_y; y_x = x_y }

        /// Create a 8D hypersphere vector
        let create8DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point8D, float> =
            let values = ResizeArray<SparseValue<Point8D, float>>()

            // For high dimensions, we need to be even more selective
            let pointsToCheck = min d (max 5 (1000 / 8))

            // We'll use a regular sample to explore the space
            let stepSize = max 1 (d / pointsToCheck)

            // Use a recursive approach for high dimensions
            let rec generatePoints (coords: int list) (dimension: int) =
                if dimension = 8 then
                    // Convert coordinates to normalized space
                    let normalizedCoords =
                        coords
                        |> List.rev
                        |> List.map (fun idx -> normalizeIndex d idx)
                        |> List.toArray

                    // Check if point is on the hypersphere
                    if isNearHypersphere radius epsilon normalizedCoords then
                        let point =
                            let coords = List.rev coords
                            {
                                x0 = List.item 0 coords
                                x1 = List.item 1 coords
                                x2 = List.item 2 coords
                                x3 = List.item 3 coords
                                x4 = List.item 4 coords
                                x5 = List.item 5 coords
                                x6 = List.item 6 coords
                                x7 = List.item 7 coords
                            }
                        values.Add({ x = point; value = 1.0 })
                else
                    for i in 0 .. stepSize .. (d-1) do
                        generatePoints (i :: coords) (dimension + 1)

            generatePoints [] 0

            SparseArray.create (values.ToArray())

    open Helpers

    /// Run a common performance test template for any dimension
    let runGenericPerformanceTest<'TPoint when 'TPoint : equality and 'TPoint : comparison>
            (writeLine : string -> unit)
            (d: int)
            (k: int)
            (createMatrix: int -> float -> SparseMatrix<'TPoint, float>)
            (createVector: int -> float -> float -> SparseArray<'TPoint, float>) =
        // Parameters
        let a = 0.5   // Diagonal element value
        let radius = 0.7 // Hypersphere radius
        let epsilon = 0.05 // Thickness of the hypersphere

        writeLine($"Starting {k}D performance test with d={d}...")

        // Create the matrix
        let stopwatch = Stopwatch.StartNew()
        let matrix = createMatrix d a
        let creationTime = stopwatch.ElapsedMilliseconds

        // Create the vector
        writeLine("Creating hypersphere vector...")
        stopwatch.Restart()
        let vector = createVector d radius epsilon
        let vectorCreationTime = stopwatch.ElapsedMilliseconds

        // Count non-zero elements
        let matrixSize = totalSize d k
        let vectorNonZeros = vector.getValues() |> Seq.length

        // Perform multiplication
        writeLine("Starting matrix-vector multiplication...")
        stopwatch.Restart()
        let result = matrix * vector
        let multiplicationTime = stopwatch.ElapsedMilliseconds

        // Count result non-zeros
        let resultNonZeros = result.getValues() |> Seq.length

        // Report memory usage
        let currentProcess = Process.GetCurrentProcess()
        let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)

        // Output performance metrics
        writeLine($"{k}D Performance Test (d={d}):")
        writeLine($"  Matrix size: {matrixSize} x {matrixSize}")
        writeLine($"  Vector non-zeros: {vectorNonZeros}")
        writeLine($"  Vector sparsity: {(float vectorNonZeros * 100.0 / float matrixSize):F6}%%")
        writeLine($"  Result non-zeros: {resultNonZeros}")
        writeLine($"  Matrix creation time: {creationTime} ms")
        writeLine($"  Vector creation time: {vectorCreationTime} ms")
        writeLine($"  Multiplication time: {multiplicationTime} ms")
        writeLine($"  Total test time: {creationTime + vectorCreationTime + multiplicationTime} ms")
        writeLine($"  Approximate memory usage: {memoryMB} MB")


    /// Performance tests for sparse matrix operations with varying dimensions
    [<Trait("Category", "Performance")>]
    type PerformanceTests(output: ITestOutputHelper) =

        [<Fact>]
        let ``2D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point2D> output.WriteLine 1000 2 create2DTridiagonalMatrix create2DHypersphereVector

        [<Fact>]
        let ``3D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point3D> output.WriteLine 500 3 create3DTridiagonalMatrix create3DHypersphereVector

        [<Fact>]
        let ``4D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point4D> output.WriteLine 100 4 create4DTridiagonalMatrix create4DHypersphereVector

        [<Fact>]
        let ``5D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point5D> output.WriteLine 50 5 create5DTridiagonalMatrix create5DHypersphereVector

        [<Fact>]
        let ``6D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point6D> output.WriteLine 25 6 create6DTridiagonalMatrix create6DHypersphereVector

        [<Fact>]
        let ``7D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point7D> output.WriteLine 20 7 create7DTridiagonalMatrix create7DHypersphereVector

        [<Fact>]
        let ``8D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point8D> output.WriteLine 15 8 create8DTridiagonalMatrix create8DHypersphereVector

        [<Fact>]
        let ``Calculate Non-Zeros for 1D Tridiagonal Matrix``() =
            // 1D case - d=5, k=1
            let d = 5
            let k = 1
            let expectedCount = 13L // 5 diagonal + 8 off-diagonal elements (standard tridiagonal)

            // Calculate using formula
            let calculatedCount = calculateTridiagonalNonZeros d k
            calculatedCount.Should().Be(expectedCount, $"formula should calculate correct number of non-zeros for d = {d}, k = {k}") |> ignore

        [<Fact>]
        let ``Calculate Non-Zeros for 2D Tridiagonal Matrix``() =
            // Small case - d=3, k=2
            let d = 3
            let k = 2
            let expectedCount = 33L // 9 diagonal + 24 off-diagonal elements

            // Calculate using formula
            let calculatedCount = calculateTridiagonalNonZeros d k
            calculatedCount.Should().Be(expectedCount, $"formula should calculate correct number of non-zeros for d = {d}, k = {k}") |> ignore

            // Create actual matrix and count non-zeros
            let matrix = create2DTridiagonalMatrix d 0.5

            // Count actual non-zeros
            let mutable actualCount = 0L
            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    let point = { i0 = i; i1 = j }
                    let connections = matrix.x_y(point).getValues() |> Seq.length
                    actualCount <- actualCount + int64 connections

            actualCount.Should().Be(expectedCount, $"actual matrix should have expected non-zeros for d = {d}, k = {k}") |> ignore

        [<Fact>]
        let ``Calculate Non-Zeros for 3D Tridiagonal Matrix``() =
            // Small case - d=3, k=3
            let d = 3
            let k = 3
            let expectedCount = 135L // 27 diagonal + 108 off-diagonal elements

            // Calculate using formula
            let calculatedCount = calculateTridiagonalNonZeros d k
            calculatedCount.Should().Be(expectedCount, $"formula should calculate correct number of non-zeros for d = {d}, k = {k}") |> ignore

            // Create actual matrix and count non-zeros
            let matrix = create3DTridiagonalMatrix d 0.5

            // Count actual non-zeros
            let mutable actualCount = 0L
            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    for m in 0..(d-1) do
                        let point = { i0 = i; i1 = j; i2 = m }
                        let connections = matrix.x_y(point).getValues() |> Seq.length
                        actualCount <- actualCount + int64 connections

            actualCount.Should().Be(expectedCount, $"actual matrix should have expected non-zeros for d = {d}, k = {k}") |> ignore

        [<Fact>]
        let ``Calculate Non-Zeros for 4D Tridiagonal Matrix``() =
            // Small case - d=3, k=4
            let d = 3
            let k = 4
            let expectedCount = 513L // 81 diagonal + 432 off-diagonal elements

            // Calculate using formula
            let calculatedCount = calculateTridiagonalNonZeros d k
            calculatedCount.Should().Be(expectedCount, $"formula should calculate correct number of non-zeros for d = {d}, k = {k}") |> ignore

            // Create actual matrix and count non-zeros
            let matrix = create4DTridiagonalMatrix d 0.5

            // Count actual non-zeros
            let mutable actualCount = 0L
            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    for m in 0..(d-1) do
                        for n in 0..(d-1) do
                            let point = { i0 = i; i1 = j; i2 = m; i3 = n }
                            let connections = matrix.x_y(point).getValues() |> Seq.length
                            actualCount <- actualCount + int64 connections

            actualCount.Should().Be(expectedCount, $"actual matrix should have expected non-zeros for d = {d}, k = {k}") |> ignore

        [<Fact>]
        let ``Calculate Non-Zeros for Various Matrix Sizes``() =
            // Test various combinations
            let testCases = [
                // d, k, expected
                (2, 1, 4L)    // 2 diagonal + 2 off-diagonal (1D)
                (5, 1, 13L)   // 5 diagonal + 8 off-diagonal (1D)
                (2, 2, 12L)   // 4 diagonal + 8 off-diagonal
                (4, 2, 64L)   // 16 diagonal + 48 off-diagonal
                (5, 2, 105L)   // 25 diagonal + 80 off-diagonal
                (2, 3, 32L)   // 8 diagonal + 24 off-diagonal
                (4, 3, 352L)  // 64 diagonal + 388 off-diagonal
            ]

            use _ = new AssertionScope()

            for (d, k, expected) in testCases do
                let calculatedCount = calculateTridiagonalNonZeros d k
                calculatedCount.Should().Be(expected, $"formula should calculate correct non-zeros for d = {d}, k = {k}") |> ignore
