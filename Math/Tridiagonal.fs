namespace Softellect.Math

open Softellect.Math.Primitives
open Softellect.Math.Sparse

/// Generated module for tridiagonal sparse matrices.
/// See: https://claude.ai/chat/75221a0e-895f-4d27-b5d5-6d8ce80974bd
module Tridiagonal =

    /// Create a 2D tridiagonal sparse matrix with optimized performance for random walk probabilities
    let createTridiagonalMatrix2D (d: int) (a: float) : SparseMatrix<Point2D, float> =
        // Parameter validation
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range (0, 1)"

        // Dimension for 2D
        let k = 2

        // Calculate base b for internal points based on the constraint a + 2*k*b = 1
        let baseB = (1.0 - a) / (2.0 * float k)

        // Precompute probability configurations for all possible boundary cases (k+1 = 3 cases)
        // Case 0: Internal point (touching 0 boundaries) - original a and b
        let internal_a = a
        let internal_b = baseB

        // Case 1: Point touching 1 boundary (3 neighbors instead of 4)
        let boundary1_a = a * (2.0 * float k - 1.0) / (2.0 * float k - 1.0 + a)
        let boundary1_b = (1.0 - boundary1_a) / (2.0 * float k - 1.0)

        // Case 2: Point touching 2 boundaries (corner, 2 neighbors)
        let boundary2_a = a * (2.0 * float k - 2.0) / (2.0 * float k - 2.0 + a)
        let boundary2_b = (1.0 - boundary2_a) / (2.0 * float k - 2.0)

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point2D) =
            let values = ResizeArray<SparseValue<Point2D, float>>()

            // Count how many boundaries this point touches
            let boundaryCount =
                (if point.x0 = 0 || point.x0 = d - 1 then 1 else 0) +
                (if point.x1 = 0 || point.x1 = d - 1 then 1 else 0)

            // Select appropriate probability values based on boundary count
            let (stay_prob, move_prob) =
                match boundaryCount with
                | 0 -> (internal_a, internal_b)
                | 1 -> (boundary1_a, boundary1_b)
                | 2 -> (boundary2_a, boundary2_b)
                | _ -> failwith "Invalid boundary count"

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.x0 > 0 then values.Add({ x = { point with x0 = point.x0 - 1 }; value = move_prob })
            if point.x0 < d - 1 then values.Add({ x = { point with x0 = point.x0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.x1 > 0 then values.Add({ x = { point with x1 = point.x1 - 1 }; value = move_prob })
            if point.x1 < d - 1 then values.Add({ x = { point with x1 = point.x1 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        // x_y function: essentially transposition of y_x
        { x_y = y_x; y_x = y_x }

    /// Create a 3D tridiagonal sparse matrix with optimized performance for random walk probabilities
    let createTridiagonalMatrix3D (d: int) (a: float) : SparseMatrix<Point3D, float> =
        // Parameter validation
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range (0, 1)"

        // Dimension for 3D
        let k = 3

        // Calculate base b for internal points based on the constraint a + 2*k*b = 1
        let baseB = (1.0 - a) / (2.0 * float k)

        // Precompute probability configurations for all possible boundary cases (k+1 = 4 cases)
        // Case 0: Internal point (touching 0 boundaries) - original a and b
        let internal_a = a
        let internal_b = baseB

        // Case 1: Point touching 1 boundary (5 neighbors instead of 6)
        // Keep the ratio between a and b the same: a / (2k*b) = a_1 / (2k-1)*b_1
        // With constraint a_1 + (2k-1)*b_1 = 1
        let boundary1_a = a * (2.0 * float k - 1.0) / (2.0 * float k - 1.0 + a)
        let boundary1_b = (1.0 - boundary1_a) / (2.0 * float k - 1.0)

        // Case 2: Point touching 2 boundaries (4 neighbors)
        let boundary2_a = a * (2.0 * float k - 2.0) / (2.0 * float k - 2.0 + a)
        let boundary2_b = (1.0 - boundary2_a) / (2.0 * float k - 2.0)

        // Case 3: Point touching 3 boundaries (corner, 3 neighbors)
        let boundary3_a = a * (2.0 * float k - 3.0) / (2.0 * float k - 3.0 + a)
        let boundary3_b = (1.0 - boundary3_a) / (2.0 * float k - 3.0)

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point3D) =
            let values = ResizeArray<SparseValue<Point3D, float>>()

            // Count how many boundaries this point touches
            let boundaryCount =
                (if point.x0 = 0 || point.x0 = d - 1 then 1 else 0) +
                (if point.x1 = 0 || point.x1 = d - 1 then 1 else 0) +
                (if point.x2 = 0 || point.x2 = d - 1 then 1 else 0)

            // Select appropriate probability values based on boundary count
            let (stay_prob, move_prob) =
                match boundaryCount with
                | 0 -> (internal_a, internal_b)
                | 1 -> (boundary1_a, boundary1_b)
                | 2 -> (boundary2_a, boundary2_b)
                | 3 -> (boundary3_a, boundary3_b)
                | _ -> failwith "Invalid boundary count"

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.x0 > 0 then values.Add({ x = { point with x0 = point.x0 - 1 }; value = move_prob })
            if point.x0 < d - 1 then values.Add({ x = { point with x0 = point.x0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.x1 > 0 then values.Add({ x = { point with x1 = point.x1 - 1 }; value = move_prob })
            if point.x1 < d - 1 then values.Add({ x = { point with x1 = point.x1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.x2 > 0 then values.Add({ x = { point with x2 = point.x2 - 1 }; value = move_prob })
            if point.x2 < d - 1 then values.Add({ x = { point with x2 = point.x2 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }

    /// Create a 4D tridiagonal sparse matrix with optimized performance for random walk probabilities
    let createTridiagonalMatrix4D (d: int) (a: float) : SparseMatrix<Point4D, float> =
        // Parameter validation
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range (0, 1)"

        // Dimension for 4D
        let k = 4

        // Calculate base b for internal points based on the constraint a + 2*k*b = 1
        let baseB = (1.0 - a) / (2.0 * float k)

        // Precompute probability configurations for all possible boundary cases (k+1 = 5 cases)
        // Case 0: Internal point (touching 0 boundaries) - original a and b
        let internal_a = a
        let internal_b = baseB

        // Cases 1-4: Points touching 1-4 boundaries
        let boundaryConfigs = Array.init (k + 1) (fun i ->
            if i = 0 then (internal_a, internal_b)
            else
                let adjNbCount = 2.0 * float k - float i
                let adj_a = a * adjNbCount / (adjNbCount + a)
                let adj_b = (1.0 - adj_a) / adjNbCount
                (adj_a, adj_b)
        )

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point4D) =
            let values = ResizeArray<SparseValue<Point4D, float>>()

            // Count how many boundaries this point touches
            let boundaryCount =
                (if point.x0 = 0 || point.x0 = d - 1 then 1 else 0) +
                (if point.x1 = 0 || point.x1 = d - 1 then 1 else 0) +
                (if point.x2 = 0 || point.x2 = d - 1 then 1 else 0) +
                (if point.x3 = 0 || point.x3 = d - 1 then 1 else 0)

            // Select appropriate probability values based on boundary count
            let (stay_prob, move_prob) = boundaryConfigs.[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.x0 > 0 then values.Add({ x = { point with x0 = point.x0 - 1 }; value = move_prob })
            if point.x0 < d - 1 then values.Add({ x = { point with x0 = point.x0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.x1 > 0 then values.Add({ x = { point with x1 = point.x1 - 1 }; value = move_prob })
            if point.x1 < d - 1 then values.Add({ x = { point with x1 = point.x1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.x2 > 0 then values.Add({ x = { point with x2 = point.x2 - 1 }; value = move_prob })
            if point.x2 < d - 1 then values.Add({ x = { point with x2 = point.x2 + 1 }; value = move_prob })

            // Off-diagonal in x3 direction
            if point.x3 > 0 then values.Add({ x = { point with x3 = point.x3 - 1 }; value = move_prob })
            if point.x3 < d - 1 then values.Add({ x = { point with x3 = point.x3 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }

    /// Create a 5D tridiagonal sparse matrix with optimized performance for random walk probabilities
    let createTridiagonalMatrix5D (d: int) (a: float) : SparseMatrix<Point5D, float> =
        // Parameter validation
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range (0, 1)"

        // Dimension for 5D
        let k = 5

        // Calculate base b for internal points based on the constraint a + 2*k*b = 1
        let baseB = (1.0 - a) / (2.0 * float k)

        // Precompute probability configurations for all possible boundary cases (k+1 = 6 cases)
        let boundaryConfigs = Array.init (k + 1) (fun i ->
            if i = 0 then (a, baseB)
            else
                let adjNbCount = 2.0 * float k - float i
                let adj_a = a * adjNbCount / (adjNbCount + a)
                let adj_b = (1.0 - adj_a) / adjNbCount
                (adj_a, adj_b)
        )

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point5D) =
            let values = ResizeArray<SparseValue<Point5D, float>>()

            // Count how many boundaries this point touches
            let boundaryCount =
                (if point.x0 = 0 || point.x0 = d - 1 then 1 else 0) +
                (if point.x1 = 0 || point.x1 = d - 1 then 1 else 0) +
                (if point.x2 = 0 || point.x2 = d - 1 then 1 else 0) +
                (if point.x3 = 0 || point.x3 = d - 1 then 1 else 0) +
                (if point.x4 = 0 || point.x4 = d - 1 then 1 else 0)

            // Select appropriate probability values based on boundary count
            let (stay_prob, move_prob) = boundaryConfigs.[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.x0 > 0 then values.Add({ x = { point with x0 = point.x0 - 1 }; value = move_prob })
            if point.x0 < d - 1 then values.Add({ x = { point with x0 = point.x0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.x1 > 0 then values.Add({ x = { point with x1 = point.x1 - 1 }; value = move_prob })
            if point.x1 < d - 1 then values.Add({ x = { point with x1 = point.x1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.x2 > 0 then values.Add({ x = { point with x2 = point.x2 - 1 }; value = move_prob })
            if point.x2 < d - 1 then values.Add({ x = { point with x2 = point.x2 + 1 }; value = move_prob })

            // Off-diagonal in x3 direction
            if point.x3 > 0 then values.Add({ x = { point with x3 = point.x3 - 1 }; value = move_prob })
            if point.x3 < d - 1 then values.Add({ x = { point with x3 = point.x3 + 1 }; value = move_prob })

            // Off-diagonal in x4 direction
            if point.x4 > 0 then values.Add({ x = { point with x4 = point.x4 - 1 }; value = move_prob })
            if point.x4 < d - 1 then values.Add({ x = { point with x4 = point.x4 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }

    /// Create a 6D tridiagonal sparse matrix with optimized performance for random walk probabilities
    let createTridiagonalMatrix6D (d: int) (a: float) : SparseMatrix<Point6D, float> =
        // Parameter validation
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range (0, 1)"

        // Dimension for 6D
        let k = 6

        // Calculate base b for internal points based on the constraint a + 2*k*b = 1
        let baseB = (1.0 - a) / (2.0 * float k)

        // Precompute probability configurations for all possible boundary cases (k+1 = 7 cases)
        let boundaryConfigs = Array.init (k + 1) (fun i ->
            if i = 0 then (a, baseB)
            else
                let adjNbCount = 2.0 * float k - float i
                let adj_a = a * adjNbCount / (adjNbCount + a)
                let adj_b = (1.0 - adj_a) / adjNbCount
                (adj_a, adj_b)
        )

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point6D) =
            let values = ResizeArray<SparseValue<Point6D, float>>()

            // Count how many boundaries this point touches
            let boundaryCount =
                (if point.x0 = 0 || point.x0 = d - 1 then 1 else 0) +
                (if point.x1 = 0 || point.x1 = d - 1 then 1 else 0) +
                (if point.x2 = 0 || point.x2 = d - 1 then 1 else 0) +
                (if point.x3 = 0 || point.x3 = d - 1 then 1 else 0) +
                (if point.x4 = 0 || point.x4 = d - 1 then 1 else 0) +
                (if point.x5 = 0 || point.x5 = d - 1 then 1 else 0)

            // Select appropriate probability values based on boundary count
            let (stay_prob, move_prob) = boundaryConfigs.[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.x0 > 0 then values.Add({ x = { point with x0 = point.x0 - 1 }; value = move_prob })
            if point.x0 < d - 1 then values.Add({ x = { point with x0 = point.x0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.x1 > 0 then values.Add({ x = { point with x1 = point.x1 - 1 }; value = move_prob })
            if point.x1 < d - 1 then values.Add({ x = { point with x1 = point.x1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.x2 > 0 then values.Add({ x = { point with x2 = point.x2 - 1 }; value = move_prob })
            if point.x2 < d - 1 then values.Add({ x = { point with x2 = point.x2 + 1 }; value = move_prob })

            // Off-diagonal in x3 direction
            if point.x3 > 0 then values.Add({ x = { point with x3 = point.x3 - 1 }; value = move_prob })
            if point.x3 < d - 1 then values.Add({ x = { point with x3 = point.x3 + 1 }; value = move_prob })

            // Off-diagonal in x4 direction
            if point.x4 > 0 then values.Add({ x = { point with x4 = point.x4 - 1 }; value = move_prob })
            if point.x4 < d - 1 then values.Add({ x = { point with x4 = point.x4 + 1 }; value = move_prob })

            // Off-diagonal in x5 direction
            if point.x5 > 0 then values.Add({ x = { point with x5 = point.x5 - 1 }; value = move_prob })
            if point.x5 < d - 1 then values.Add({ x = { point with x5 = point.x5 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }

    /// Create a 7D tridiagonal sparse matrix with optimized performance for random walk probabilities
    let createTridiagonalMatrix7D (d: int) (a: float) : SparseMatrix<Point7D, float> =
        // Parameter validation
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range (0, 1)"

        // Dimension for 7D
        let k = 7

        // Calculate base b for internal points based on the constraint a + 2*k*b = 1
        let baseB = (1.0 - a) / (2.0 * float k)

        // Precompute probability configurations for all possible boundary cases (k+1 = 8 cases)
        let boundaryConfigs = Array.init (k + 1) (fun i ->
            if i = 0 then (a, baseB)
            else
                let adjNbCount = 2.0 * float k - float i
                let adj_a = a * adjNbCount / (adjNbCount + a)
                let adj_b = (1.0 - adj_a) / adjNbCount
                (adj_a, adj_b)
        )

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point7D) =
            let values = ResizeArray<SparseValue<Point7D, float>>()

            // Count how many boundaries this point touches
            let boundaryCount =
                (if point.x0 = 0 || point.x0 = d - 1 then 1 else 0) +
                (if point.x1 = 0 || point.x1 = d - 1 then 1 else 0) +
                (if point.x2 = 0 || point.x2 = d - 1 then 1 else 0) +
                (if point.x3 = 0 || point.x3 = d - 1 then 1 else 0) +
                (if point.x4 = 0 || point.x4 = d - 1 then 1 else 0) +
                (if point.x5 = 0 || point.x5 = d - 1 then 1 else 0) +
                (if point.x6 = 0 || point.x6 = d - 1 then 1 else 0)

            // Select appropriate probability values based on boundary count
            let (stay_prob, move_prob) = boundaryConfigs.[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.x0 > 0 then values.Add({ x = { point with x0 = point.x0 - 1 }; value = move_prob })
            if point.x0 < d - 1 then values.Add({ x = { point with x0 = point.x0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.x1 > 0 then values.Add({ x = { point with x1 = point.x1 - 1 }; value = move_prob })
            if point.x1 < d - 1 then values.Add({ x = { point with x1 = point.x1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.x2 > 0 then values.Add({ x = { point with x2 = point.x2 - 1 }; value = move_prob })
            if point.x2 < d - 1 then values.Add({ x = { point with x2 = point.x2 + 1 }; value = move_prob })

            // Off-diagonal in x3 direction
            if point.x3 > 0 then values.Add({ x = { point with x3 = point.x3 - 1 }; value = move_prob })
            if point.x3 < d - 1 then values.Add({ x = { point with x3 = point.x3 + 1 }; value = move_prob })

            // Off-diagonal in x4 direction
            if point.x4 > 0 then values.Add({ x = { point with x4 = point.x4 - 1 }; value = move_prob })
            if point.x4 < d - 1 then values.Add({ x = { point with x4 = point.x4 + 1 }; value = move_prob })

            // Off-diagonal in x5 direction
            if point.x5 > 0 then values.Add({ x = { point with x5 = point.x5 - 1 }; value = move_prob })
            if point.x5 < d - 1 then values.Add({ x = { point with x5 = point.x5 + 1 }; value = move_prob })

            // Off-diagonal in x6 direction
            if point.x6 > 0 then values.Add({ x = { point with x6 = point.x6 - 1 }; value = move_prob })
            if point.x6 < d - 1 then values.Add({ x = { point with x6 = point.x6 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }

    /// Create a 8D tridiagonal sparse matrix with optimized performance for random walk probabilities
    let createTridiagonalMatrix8D (d: int) (a: float) : SparseMatrix<Point8D, float> =
        // Parameter validation
        if a < 0.0 || a > 1.0 then failwith $"Parameter a must be in range (0, 1)"

        // Dimension for 8D
        let k = 8

        // Calculate base b for internal points based on the constraint a + 2*k*b = 1
        let baseB = (1.0 - a) / (2.0 * float k)

        // Precompute probability configurations for all possible boundary cases (k+1 = 9 cases)
        let boundaryConfigs = Array.init (k + 1) (fun i ->
            if i = 0 then (a, baseB)
            else
                let adjNbCount = 2.0 * float k - float i
                let adj_a = a * adjNbCount / (adjNbCount + a)
                let adj_b = (1.0 - adj_a) / adjNbCount
                (adj_a, adj_b)
        )

        // y_x function: probabilities of walking from y to x
        let y_x (point: Point8D) =
            let values = ResizeArray<SparseValue<Point8D, float>>()

            // Count how many boundaries this point touches
            let boundaryCount =
                (if point.x0 = 0 || point.x0 = d - 1 then 1 else 0) +
                (if point.x1 = 0 || point.x1 = d - 1 then 1 else 0) +
                (if point.x2 = 0 || point.x2 = d - 1 then 1 else 0) +
                (if point.x3 = 0 || point.x3 = d - 1 then 1 else 0) +
                (if point.x4 = 0 || point.x4 = d - 1 then 1 else 0) +
                (if point.x5 = 0 || point.x5 = d - 1 then 1 else 0) +
                (if point.x6 = 0 || point.x6 = d - 1 then 1 else 0) +
                (if point.x7 = 0 || point.x7 = d - 1 then 1 else 0)

            // Select appropriate probability values based on boundary count
            let (stay_prob, move_prob) = boundaryConfigs.[boundaryCount]

            // Diagonal element (self-connection)
            values.Add({ x = point; value = stay_prob })

            // Off-diagonal in x0 direction
            if point.x0 > 0 then values.Add({ x = { point with x0 = point.x0 - 1 }; value = move_prob })
            if point.x0 < d - 1 then values.Add({ x = { point with x0 = point.x0 + 1 }; value = move_prob })

            // Off-diagonal in x1 direction
            if point.x1 > 0 then values.Add({ x = { point with x1 = point.x1 - 1 }; value = move_prob })
            if point.x1 < d - 1 then values.Add({ x = { point with x1 = point.x1 + 1 }; value = move_prob })

            // Off-diagonal in x2 direction
            if point.x2 > 0 then values.Add({ x = { point with x2 = point.x2 - 1 }; value = move_prob })
            if point.x2 < d - 1 then values.Add({ x = { point with x2 = point.x2 + 1 }; value = move_prob })

            // Off-diagonal in x3 direction
            if point.x3 > 0 then values.Add({ x = { point with x3 = point.x3 - 1 }; value = move_prob })
            if point.x3 < d - 1 then values.Add({ x = { point with x3 = point.x3 + 1 }; value = move_prob })

            // Off-diagonal in x4 direction
            if point.x4 > 0 then values.Add({ x = { point with x4 = point.x4 - 1 }; value = move_prob })
            if point.x4 < d - 1 then values.Add({ x = { point with x4 = point.x4 + 1 }; value = move_prob })

            // Off-diagonal in x5 direction
            if point.x5 > 0 then values.Add({ x = { point with x5 = point.x5 - 1 }; value = move_prob })
            if point.x5 < d - 1 then values.Add({ x = { point with x5 = point.x5 + 1 }; value = move_prob })

            // Off-diagonal in x6 direction
            if point.x6 > 0 then values.Add({ x = { point with x6 = point.x6 - 1 }; value = move_prob })
            if point.x6 < d - 1 then values.Add({ x = { point with x6 = point.x6 + 1 }; value = move_prob })

            // Off-diagonal in x7 direction
            if point.x7 > 0 then values.Add({ x = { point with x7 = point.x7 - 1 }; value = move_prob })
            if point.x7 < d - 1 then values.Add({ x = { point with x7 = point.x7 + 1 }; value = move_prob })

            SparseArray.create (values.ToArray())

        { x_y = y_x; y_x = y_x }
