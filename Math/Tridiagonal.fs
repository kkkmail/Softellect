namespace Softellect.Math

open Softellect.Math.Primitives
open Softellect.Math.Sparse

/// Generated module for symmetric tridiagonal sparse matrices.
/// See: https://claude.ai/chat/75221a0e-895f-4d27-b5d5-6d8ce80974bd
///      https://claude.ai/chat/dd074aaa-3817-4232-a667-d41c36e5b593
module Tridiagonal =

    /// Discriminated Union to describe boundary probability calculation methods
    type BoundaryConfig =
        | ProportionalScaling    // Original method: a * neighbors / (neighbors + a)
        | FixedStaying           // Keep 'a' constant, redistribute to available neighbors
        | LinearScaling          // Scale 'a' by ratio of available neighbors
        | ReflectingBoundaries   // Add blocked probabilities back to staying probability


        /// Function to calculate boundary configurations based on the chosen method
        member boundaryConfig.calculate (k: int) (a: float) : (float * float)[] =
            let baseB = (1.0 - a) / (2.0 * float k)

            Array.init (k + 1) (fun i ->
                if i = 0 then
                    // Internal point - original a and b
                    (a, baseB)
                else
                    let availableNeighbors = 2.0 * float k - float i
                    let blockedNeighbors = float i

                    match boundaryConfig with
                    | ProportionalScaling ->
                        let adj_a = a * availableNeighbors / (availableNeighbors + a)
                        let adj_b = (1.0 - adj_a) / availableNeighbors
                        (adj_a, adj_b)

                    | FixedStaying ->
                        // Keep 'a' constant, redistribute blocked probability to available neighbors
                        let redistributed_prob = blockedNeighbors * baseB
                        let adj_b = (1.0 - a + redistributed_prob) / availableNeighbors
                        (a, adj_b)

                    | LinearScaling ->
                        // Simple linear scaling of 'a'
                        let adj_a = a * availableNeighbors / (2.0 * float k)
                        let adj_b = (1.0 - adj_a) / availableNeighbors
                        (adj_a, adj_b)

                    | ReflectingBoundaries ->
                        // Add blocked movement probabilities to staying probability
                        let blocked_prob = blockedNeighbors * baseB
                        let adj_a = a + blocked_prob
                        let adj_b = (1.0 - adj_a) / availableNeighbors
                        (adj_a, adj_b)
            )


/// Create a 1D tridiagonal sparse matrix with configurable boundary handling
    let createTridiagonalMatrix1D (boundaryConfig : BoundaryConfig) (d : int) (a : float) : SparseMatrix<Point1D, float> =
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range [0, 1]."
        let boundaryConfigs = boundaryConfig.calculate 1 a

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point1D) =
            let values = ResizeArray<SparseValue<Point1D, float>>()
            let boundaryCount = point.boundaryCount d
            let stay_prob, move_prob = boundaryConfigs[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.i0 > 0 then values.Add({ x = { point with i0 = point.i0 - 1 }; value = move_prob })
            if point.i0 < d - 1 then values.Add({ x = { point with i0 = point.i0 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }


    /// Create a 2D tridiagonal sparse matrix with configurable boundary handling
    let createTridiagonalMatrix2D (boundaryConfig : BoundaryConfig) (d : int) (a : float) : SparseMatrix<Point2D, float> =
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range [0, 1]."
        let boundaryConfigs = boundaryConfig.calculate 2 a

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point2D) =
            let values = ResizeArray<SparseValue<Point2D, float>>()
            let boundaryCount = point.boundaryCount d
            let stay_prob, move_prob = boundaryConfigs[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.i0 > 0 then values.Add({ x = { point with i0 = point.i0 - 1 }; value = move_prob })
            if point.i0 < d - 1 then values.Add({ x = { point with i0 = point.i0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.i1 > 0 then values.Add({ x = { point with i1 = point.i1 - 1 }; value = move_prob })
            if point.i1 < d - 1 then values.Add({ x = { point with i1 = point.i1 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }


    /// Create a 3D tridiagonal sparse matrix with configurable boundary handling
    let createTridiagonalMatrix3D (boundaryConfig : BoundaryConfig) (d : int) (a : float) : SparseMatrix<Point3D, float> =
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range [0, 1]."
        let boundaryConfigs = boundaryConfig.calculate 3 a

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point3D) =
            let values = ResizeArray<SparseValue<Point3D, float>>()
            let boundaryCount = point.boundaryCount d
            let stay_prob, move_prob = boundaryConfigs[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.i0 > 0 then values.Add({ x = { point with i0 = point.i0 - 1 }; value = move_prob })
            if point.i0 < d - 1 then values.Add({ x = { point with i0 = point.i0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.i1 > 0 then values.Add({ x = { point with i1 = point.i1 - 1 }; value = move_prob })
            if point.i1 < d - 1 then values.Add({ x = { point with i1 = point.i1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.i2 > 0 then values.Add({ x = { point with i2 = point.i2 - 1 }; value = move_prob })
            if point.i2 < d - 1 then values.Add({ x = { point with i2 = point.i2 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }


    /// Create a 4D tridiagonal sparse matrix with configurable boundary handling
    let createTridiagonalMatrix4D (boundaryConfig : BoundaryConfig) (d : int) (a : float) : SparseMatrix<Point4D, float> =
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range [0, 1]."
        let boundaryConfigs = boundaryConfig.calculate 4 a

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point4D) =
            let values = ResizeArray<SparseValue<Point4D, float>>()
            let boundaryCount = point.boundaryCount d
            let stay_prob, move_prob = boundaryConfigs[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.i0 > 0 then values.Add({ x = { point with i0 = point.i0 - 1 }; value = move_prob })
            if point.i0 < d - 1 then values.Add({ x = { point with i0 = point.i0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.i1 > 0 then values.Add({ x = { point with i1 = point.i1 - 1 }; value = move_prob })
            if point.i1 < d - 1 then values.Add({ x = { point with i1 = point.i1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.i2 > 0 then values.Add({ x = { point with i2 = point.i2 - 1 }; value = move_prob })
            if point.i2 < d - 1 then values.Add({ x = { point with i2 = point.i2 + 1 }; value = move_prob })

            // Off-diagonal in x3 direction
            if point.i3 > 0 then values.Add({ x = { point with i3 = point.i3 - 1 }; value = move_prob })
            if point.i3 < d - 1 then values.Add({ x = { point with i3 = point.i3 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }


    /// Create a 5D tridiagonal sparse matrix with configurable boundary handling
    let createTridiagonalMatrix5D (boundaryConfig : BoundaryConfig) (d : int) (a : float) : SparseMatrix<Point5D, float> =
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range [0, 1]."
        let boundaryConfigs = boundaryConfig.calculate 5 a

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point5D) =
            let values = ResizeArray<SparseValue<Point5D, float>>()
            let boundaryCount = point.boundaryCount d
            let stay_prob, move_prob = boundaryConfigs[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.i0 > 0 then values.Add({ x = { point with i0 = point.i0 - 1 }; value = move_prob })
            if point.i0 < d - 1 then values.Add({ x = { point with i0 = point.i0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.i1 > 0 then values.Add({ x = { point with i1 = point.i1 - 1 }; value = move_prob })
            if point.i1 < d - 1 then values.Add({ x = { point with i1 = point.i1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.i2 > 0 then values.Add({ x = { point with i2 = point.i2 - 1 }; value = move_prob })
            if point.i2 < d - 1 then values.Add({ x = { point with i2 = point.i2 + 1 }; value = move_prob })

            // Off-diagonal in x3 direction
            if point.i3 > 0 then values.Add({ x = { point with i3 = point.i3 - 1 }; value = move_prob })
            if point.i3 < d - 1 then values.Add({ x = { point with i3 = point.i3 + 1 }; value = move_prob })

            // Off-diagonal in x4 direction
            if point.i4 > 0 then values.Add({ x = { point with i4 = point.i4 - 1 }; value = move_prob })
            if point.i4 < d - 1 then values.Add({ x = { point with i4 = point.i4 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }


    /// Create a 6D tridiagonal sparse matrix with configurable boundary handling
    let createTridiagonalMatrix6D (boundaryConfig : BoundaryConfig) (d : int) (a : float) : SparseMatrix<Point6D, float> =
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range [0, 1]."
        let boundaryConfigs = boundaryConfig.calculate 6 a

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point6D) =
            let values = ResizeArray<SparseValue<Point6D, float>>()
            let boundaryCount = point.boundaryCount d
            let stay_prob, move_prob = boundaryConfigs[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.i0 > 0 then values.Add({ x = { point with i0 = point.i0 - 1 }; value = move_prob })
            if point.i0 < d - 1 then values.Add({ x = { point with i0 = point.i0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.i1 > 0 then values.Add({ x = { point with i1 = point.i1 - 1 }; value = move_prob })
            if point.i1 < d - 1 then values.Add({ x = { point with i1 = point.i1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.i2 > 0 then values.Add({ x = { point with i2 = point.i2 - 1 }; value = move_prob })
            if point.i2 < d - 1 then values.Add({ x = { point with i2 = point.i2 + 1 }; value = move_prob })

            // Off-diagonal in x3 direction
            if point.i3 > 0 then values.Add({ x = { point with i3 = point.i3 - 1 }; value = move_prob })
            if point.i3 < d - 1 then values.Add({ x = { point with i3 = point.i3 + 1 }; value = move_prob })

            // Off-diagonal in x4 direction
            if point.i4 > 0 then values.Add({ x = { point with i4 = point.i4 - 1 }; value = move_prob })
            if point.i4 < d - 1 then values.Add({ x = { point with i4 = point.i4 + 1 }; value = move_prob })

            // Off-diagonal in x5 direction
            if point.i5 > 0 then values.Add({ x = { point with i5 = point.i5 - 1 }; value = move_prob })
            if point.i5 < d - 1 then values.Add({ x = { point with i5 = point.i5 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }


    /// Create a 7D tridiagonal sparse matrix with configurable boundary handling
    let createTridiagonalMatrix7D (boundaryConfig : BoundaryConfig) (d : int) (a : float) : SparseMatrix<Point7D, float> =
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range [0, 1]."
        let boundaryConfigs = boundaryConfig.calculate 7 a

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point7D) =
            let values = ResizeArray<SparseValue<Point7D, float>>()
            let boundaryCount = point.boundaryCount d
            let stay_prob, move_prob = boundaryConfigs[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.i0 > 0 then values.Add({ x = { point with i0 = point.i0 - 1 }; value = move_prob })
            if point.i0 < d - 1 then values.Add({ x = { point with i0 = point.i0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.i1 > 0 then values.Add({ x = { point with i1 = point.i1 - 1 }; value = move_prob })
            if point.i1 < d - 1 then values.Add({ x = { point with i1 = point.i1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.i2 > 0 then values.Add({ x = { point with i2 = point.i2 - 1 }; value = move_prob })
            if point.i2 < d - 1 then values.Add({ x = { point with i2 = point.i2 + 1 }; value = move_prob })

            // Off-diagonal in x3 direction
            if point.i3 > 0 then values.Add({ x = { point with i3 = point.i3 - 1 }; value = move_prob })
            if point.i3 < d - 1 then values.Add({ x = { point with i3 = point.i3 + 1 }; value = move_prob })

            // Off-diagonal in x4 direction
            if point.i4 > 0 then values.Add({ x = { point with i4 = point.i4 - 1 }; value = move_prob })
            if point.i4 < d - 1 then values.Add({ x = { point with i4 = point.i4 + 1 }; value = move_prob })

            // Off-diagonal in x5 direction
            if point.i5 > 0 then values.Add({ x = { point with i5 = point.i5 - 1 }; value = move_prob })
            if point.i5 < d - 1 then values.Add({ x = { point with i5 = point.i5 + 1 }; value = move_prob })

            // Off-diagonal in x6 direction
            if point.i6 > 0 then values.Add({ x = { point with i6 = point.i6 - 1 }; value = move_prob })
            if point.i6 < d - 1 then values.Add({ x = { point with i6 = point.i6 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }


    /// Create a 8D tridiagonal sparse matrix with configurable boundary handling
    let createTridiagonalMatrix8D (boundaryConfig : BoundaryConfig) (d : int) (a : float) : SparseMatrix<Point8D, float> =
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range [0, 1]."
        let boundaryConfigs = boundaryConfig.calculate 8 a

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point8D) =
            let values = ResizeArray<SparseValue<Point8D, float>>()
            let boundaryCount = point.boundaryCount d
            let stay_prob, move_prob = boundaryConfigs[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.i0 > 0 then values.Add({ x = { point with i0 = point.i0 - 1 }; value = move_prob })
            if point.i0 < d - 1 then values.Add({ x = { point with i0 = point.i0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.i1 > 0 then values.Add({ x = { point with i1 = point.i1 - 1 }; value = move_prob })
            if point.i1 < d - 1 then values.Add({ x = { point with i1 = point.i1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.i2 > 0 then values.Add({ x = { point with i2 = point.i2 - 1 }; value = move_prob })
            if point.i2 < d - 1 then values.Add({ x = { point with i2 = point.i2 + 1 }; value = move_prob })

            // Off-diagonal in x3 direction
            if point.i3 > 0 then values.Add({ x = { point with i3 = point.i3 - 1 }; value = move_prob })
            if point.i3 < d - 1 then values.Add({ x = { point with i3 = point.i3 + 1 }; value = move_prob })

            // Off-diagonal in x4 direction
            if point.i4 > 0 then values.Add({ x = { point with i4 = point.i4 - 1 }; value = move_prob })
            if point.i4 < d - 1 then values.Add({ x = { point with i4 = point.i4 + 1 }; value = move_prob })

            // Off-diagonal in x5 direction
            if point.i5 > 0 then values.Add({ x = { point with i5 = point.i5 - 1 }; value = move_prob })
            if point.i5 < d - 1 then values.Add({ x = { point with i5 = point.i5 + 1 }; value = move_prob })

            // Off-diagonal in x6 direction
            if point.i6 > 0 then values.Add({ x = { point with i6 = point.i6 - 1 }; value = move_prob })
            if point.i6 < d - 1 then values.Add({ x = { point with i6 = point.i6 + 1 }; value = move_prob })

            // Off-diagonal in x7 direction
            if point.i7 > 0 then values.Add({ x = { point with i7 = point.i7 - 1 }; value = move_prob })
            if point.i7 < d - 1 then values.Add({ x = { point with i7 = point.i7 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }
