namespace Softellect.Tests.MathTests

open System
open System.Diagnostics
open Xunit
open Xunit.Abstractions
open Softellect.Math.Primitives
open Softellect.Math.Sparse

module MultidimensionalRecordTests =
    /// Helper functions for creating multi-dimensional test data
    module Helpers =
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

        // 2D Matrix and Vector Functions

        /// Create a 2D tridiagonal sparse matrix
        let create2DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point2D, float> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith $"Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the matrix as functions (no full instantiation)
            let x_y point =
                let values = ResizeArray<SparseValue<Point2D, float>>()

                // Diagonal element (self-connection)
                values.Add({ x = point; value = a })

                // Off-diagonal in x0 direction
                if point.x0 > 0 then
                    values.Add({ x = { x0 = point.x0 - 1; x1 = point.x1 }; value = b })

                if point.x0 < d - 1 then
                    values.Add({ x = { x0 = point.x0 + 1; x1 = point.x1 }; value = b })

                // Off-diagonal in x1 direction
                if point.x1 > 0 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1 - 1 }; value = b })

                if point.x1 < d - 1 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1 + 1 }; value = b })

                SparseArray.create (values.ToArray())

            { x_y = x_y; y_x = x_y }

        /// Create a 2D hypersphere vector
        let create2DHypersphereVector (d: int) (radius: float) (epsilon: float) : SparseArray<Point2D, float> =
            let values = ResizeArray<SparseValue<Point2D, float>>()

            for i in 0..(d-1) do
                for j in 0..(d-1) do
                    let x = normalizeIndex d i
                    let y = normalizeIndex d j

                    // Check if point is on the hypersphere (circle in 2D)
                    if isNearHypersphere radius epsilon [|x; y|] then
                        values.Add({ x = { x0 = i; x1 = j }; value = 1.0 })

            SparseArray.create (values.ToArray())

        // 3D Matrix and Vector Functions

        /// Create a 3D tridiagonal sparse matrix
        let create3DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point3D, float> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith $"Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the matrix as functions (no full instantiation)
            let x_y point =
                let values = ResizeArray<SparseValue<Point3D, float>>()

                // Diagonal element (self-connection)
                values.Add({ x = point; value = a })

                // Off-diagonal in x0 direction
                if point.x0 > 0 then
                    values.Add({ x = { x0 = point.x0 - 1; x1 = point.x1; x2 = point.x2 }; value = b })

                if point.x0 < d - 1 then
                    values.Add({ x = { x0 = point.x0 + 1; x1 = point.x1; x2 = point.x2 }; value = b })

                // Off-diagonal in x1 direction
                if point.x1 > 0 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1 - 1; x2 = point.x2 }; value = b })

                if point.x1 < d - 1 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1 + 1; x2 = point.x2 }; value = b })

                // Off-diagonal in x2 direction
                if point.x2 > 0 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2 - 1 }; value = b })

                if point.x2 < d - 1 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2 + 1 }; value = b })

                SparseArray.create (values.ToArray())

            { x_y = x_y; y_x = x_y }

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
                            values.Add({ x = { x0 = i; x1 = j; x2 = k }; value = 1.0 })

            SparseArray.create (values.ToArray())

        // 4D Matrix and Vector Functions

        /// Create a 4D tridiagonal sparse matrix
        let create4DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point4D, float> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith $"Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the matrix as functions (no full instantiation)
            let x_y point =
                let values = ResizeArray<SparseValue<Point4D, float>>()

                // Diagonal element (self-connection)
                values.Add({ x = point; value = a })

                // Off-diagonal in x0 direction
                if point.x0 > 0 then
                    values.Add({ x = { x0 = point.x0 - 1; x1 = point.x1; x2 = point.x2; x3 = point.x3 }; value = b })

                if point.x0 < d - 1 then
                    values.Add({ x = { x0 = point.x0 + 1; x1 = point.x1; x2 = point.x2; x3 = point.x3 }; value = b })

                // Off-diagonal in x1 direction
                if point.x1 > 0 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1 - 1; x2 = point.x2; x3 = point.x3 }; value = b })

                if point.x1 < d - 1 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1 + 1; x2 = point.x2; x3 = point.x3 }; value = b })

                // Off-diagonal in x2 direction
                if point.x2 > 0 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2 - 1; x3 = point.x3 }; value = b })

                if point.x2 < d - 1 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2 + 1; x3 = point.x3 }; value = b })

                // Off-diagonal in x3 direction
                if point.x3 > 0 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2; x3 = point.x3 - 1 }; value = b })

                if point.x3 < d - 1 then
                    values.Add({ x = { x0 = point.x0; x1 = point.x1; x2 = point.x2; x3 = point.x3 + 1 }; value = b })

                SparseArray.create (values.ToArray())

            { x_y = x_y; y_x = x_y }

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
                                values.Add({ x = { x0 = i; x1 = j; x2 = k; x3 = l }; value = 1.0 })

            SparseArray.create (values.ToArray())

        // 5D Matrix and Vector Functions

        /// Create a 5D tridiagonal sparse matrix
        let create5DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point5D, float> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith $"Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the matrix as functions (no full instantiation)
            let x_y point =
                let values = ResizeArray<SparseValue<Point5D, float>>()

                // Diagonal element (self-connection)
                values.Add({ x = point; value = a })

                // Off-diagonal in x0 direction
                if point.x0 > 0 then
                    values.Add({ x = { point with x0 = point.x0 - 1 }; value = b })

                if point.x0 < d - 1 then
                    values.Add({ x = { point with x0 = point.x0 + 1 }; value = b })

                // Off-diagonal in x1 direction
                if point.x1 > 0 then
                    values.Add({ x = { point with x1 = point.x1 - 1 }; value = b })

                if point.x1 < d - 1 then
                    values.Add({ x = { point with x1 = point.x1 + 1 }; value = b })

                // Off-diagonal in x2 direction
                if point.x2 > 0 then
                    values.Add({ x = { point with x2 = point.x2 - 1 }; value = b })

                if point.x2 < d - 1 then
                    values.Add({ x = { point with x2 = point.x2 + 1 }; value = b })

                // Off-diagonal in x3 direction
                if point.x3 > 0 then
                    values.Add({ x = { point with x3 = point.x3 - 1 }; value = b })

                if point.x3 < d - 1 then
                    values.Add({ x = { point with x3 = point.x3 + 1 }; value = b })

                // Off-diagonal in x4 direction
                if point.x4 > 0 then
                    values.Add({ x = { point with x4 = point.x4 - 1 }; value = b })

                if point.x4 < d - 1 then
                    values.Add({ x = { point with x4 = point.x4 + 1 }; value = b })

                SparseArray.create (values.ToArray())

            { x_y = x_y; y_x = x_y }

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

        /// Create a 6D tridiagonal sparse matrix
        let create6DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point6D, float> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith $"Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the matrix as functions (no full instantiation)
            let x_y point =
                let values = ResizeArray<SparseValue<Point6D, float>>()

                // Diagonal element (self-connection)
                values.Add({ x = point; value = a })

                // Off-diagonal in x0 direction
                if point.x0 > 0 then
                    values.Add({ x = { point with x0 = point.x0 - 1 }; value = b })

                if point.x0 < d - 1 then
                    values.Add({ x = { point with x0 = point.x0 + 1 }; value = b })

                // Off-diagonal in x1 direction
                if point.x1 > 0 then
                    values.Add({ x = { point with x1 = point.x1 - 1 }; value = b })

                if point.x1 < d - 1 then
                    values.Add({ x = { point with x1 = point.x1 + 1 }; value = b })

                // Off-diagonal in x2 direction
                if point.x2 > 0 then
                    values.Add({ x = { point with x2 = point.x2 - 1 }; value = b })

                if point.x2 < d - 1 then
                    values.Add({ x = { point with x2 = point.x2 + 1 }; value = b })

                // Off-diagonal in x3 direction
                if point.x3 > 0 then
                    values.Add({ x = { point with x3 = point.x3 - 1 }; value = b })

                if point.x3 < d - 1 then
                    values.Add({ x = { point with x3 = point.x3 + 1 }; value = b })

                // Off-diagonal in x4 direction
                if point.x4 > 0 then
                    values.Add({ x = { point with x4 = point.x4 - 1 }; value = b })

                if point.x4 < d - 1 then
                    values.Add({ x = { point with x4 = point.x4 + 1 }; value = b })

                // Off-diagonal in x5 direction
                if point.x5 > 0 then
                    values.Add({ x = { point with x5 = point.x5 - 1 }; value = b })

                if point.x5 < d - 1 then
                    values.Add({ x = { point with x5 = point.x5 + 1 }; value = b })

                SparseArray.create (values.ToArray())

            { x_y = x_y; y_x = x_y }

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

        /// Create a 7D tridiagonal sparse matrix
        let create7DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point7D, float> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith $"Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the matrix as functions (no full instantiation)
            let x_y point =
                let values = ResizeArray<SparseValue<Point7D, float>>()

                // Diagonal element (self-connection)
                values.Add({ x = point; value = a })

                // Off-diagonal in x0 direction
                if point.x0 > 0 then
                    values.Add({ x = { point with x0 = point.x0 - 1 }; value = b })

                if point.x0 < d - 1 then
                    values.Add({ x = { point with x0 = point.x0 + 1 }; value = b })

                // Off-diagonal in x1 direction
                if point.x1 > 0 then
                    values.Add({ x = { point with x1 = point.x1 - 1 }; value = b })

                if point.x1 < d - 1 then
                    values.Add({ x = { point with x1 = point.x1 + 1 }; value = b })

                // Off-diagonal in x2 direction
                if point.x2 > 0 then
                    values.Add({ x = { point with x2 = point.x2 - 1 }; value = b })

                if point.x2 < d - 1 then
                    values.Add({ x = { point with x2 = point.x2 + 1 }; value = b })

                // Off-diagonal in x3 direction
                if point.x3 > 0 then
                    values.Add({ x = { point with x3 = point.x3 - 1 }; value = b })

                if point.x3 < d - 1 then
                    values.Add({ x = { point with x3 = point.x3 + 1 }; value = b })

                // Off-diagonal in x4 direction
                if point.x4 > 0 then
                    values.Add({ x = { point with x4 = point.x4 - 1 }; value = b })

                if point.x4 < d - 1 then
                    values.Add({ x = { point with x4 = point.x4 + 1 }; value = b })

                // Off-diagonal in x5 direction
                if point.x5 > 0 then
                    values.Add({ x = { point with x5 = point.x5 - 1 }; value = b })

                if point.x5 < d - 1 then
                    values.Add({ x = { point with x5 = point.x5 + 1 }; value = b })

                // Off-diagonal in x6 direction
                if point.x6 > 0 then
                    values.Add({ x = { point with x6 = point.x6 - 1 }; value = b })

                if point.x6 < d - 1 then
                    values.Add({ x = { point with x6 = point.x6 + 1 }; value = b })

                SparseArray.create (values.ToArray())

            { x_y = x_y; y_x = x_y }

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

        /// Create a 8D tridiagonal sparse matrix
        let create8DTridiagonalMatrix (d: int) (a: float) : SparseMatrix<Point8D, float> =
            // Parameter validation
            if a <= 0.0 || a >= 1.0 then
                failwith $"Parameter a must be in range (0, 1)"

            // Calculate b based on the constraint a + 2b = 1
            let b = (1.0 - a) / 2.0

            // Create the matrix as functions (no full instantiation)
            let x_y point =
                let values = ResizeArray<SparseValue<Point8D, float>>()

                // Diagonal element (self-connection)
                values.Add({ x = point; value = a })

                // Off-diagonal in x0 direction
                if point.x0 > 0 then
                    values.Add({ x = { point with x0 = point.x0 - 1 }; value = b })

                if point.x0 < d - 1 then
                    values.Add({ x = { point with x0 = point.x0 + 1 }; value = b })

                // Off-diagonal in x1 direction
                if point.x1 > 0 then
                    values.Add({ x = { point with x1 = point.x1 - 1 }; value = b })

                if point.x1 < d - 1 then
                    values.Add({ x = { point with x1 = point.x1 + 1 }; value = b })

                // Off-diagonal in x2 direction
                if point.x2 > 0 then
                    values.Add({ x = { point with x2 = point.x2 - 1 }; value = b })

                if point.x2 < d - 1 then
                    values.Add({ x = { point with x2 = point.x2 + 1 }; value = b })

                // Off-diagonal in x3 direction
                if point.x3 > 0 then
                    values.Add({ x = { point with x3 = point.x3 - 1 }; value = b })

                if point.x3 < d - 1 then
                    values.Add({ x = { point with x3 = point.x3 + 1 }; value = b })

                // Off-diagonal in x4 direction
                if point.x4 > 0 then
                    values.Add({ x = { point with x4 = point.x4 - 1 }; value = b })

                if point.x4 < d - 1 then
                    values.Add({ x = { point with x4 = point.x4 + 1 }; value = b })

                // Off-diagonal in x5 direction
                if point.x5 > 0 then
                    values.Add({ x = { point with x5 = point.x5 - 1 }; value = b })

                if point.x5 < d - 1 then
                    values.Add({ x = { point with x5 = point.x5 + 1 }; value = b })

                // Off-diagonal in x6 direction
                if point.x6 > 0 then
                    values.Add({ x = { point with x6 = point.x6 - 1 }; value = b })

                if point.x6 < d - 1 then
                    values.Add({ x = { point with x6 = point.x6 + 1 }; value = b })

                // Off-diagonal in x7 direction
                if point.x7 > 0 then
                    values.Add({ x = { point with x7 = point.x7 - 1 }; value = b })

                if point.x7 < d - 1 then
                    values.Add({ x = { point with x7 = point.x7 + 1 }; value = b })

                SparseArray.create (values.ToArray())

            { x_y = x_y; y_x = x_y }

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
        let matrixSize = Helpers.totalSize d k
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
            runGenericPerformanceTest<Point2D> output.WriteLine 1000 2 Helpers.create2DTridiagonalMatrix Helpers.create2DHypersphereVector

        [<Fact>]
        let ``3D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point3D> output.WriteLine 500 3 Helpers.create3DTridiagonalMatrix Helpers.create3DHypersphereVector

        [<Fact>]
        let ``4D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point4D> output.WriteLine 100 4 Helpers.create4DTridiagonalMatrix Helpers.create4DHypersphereVector

        [<Fact>]
        let ``5D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point5D> output.WriteLine 50 5 Helpers.create5DTridiagonalMatrix Helpers.create5DHypersphereVector

        [<Fact>]
        let ``6D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point6D> output.WriteLine 25 6 Helpers.create6DTridiagonalMatrix Helpers.create6DHypersphereVector

        [<Fact>]
        let ``7D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point7D> output.WriteLine 20 7 Helpers.create7DTridiagonalMatrix Helpers.create7DHypersphereVector

        [<Fact>]
        let ``8D Tridiagonal Matrix Performance Test``() =
            runGenericPerformanceTest<Point8D> output.WriteLine 15 8 Helpers.create8DTridiagonalMatrix Helpers.create8DHypersphereVector
