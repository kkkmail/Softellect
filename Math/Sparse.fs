namespace Softellect.Math

open System.Collections.Generic
open FSharp.Collections
open System
open Softellect.Math.Primitives

module Sparse =

    /// Representation of non-zero value in an abstract sparse array.
    type SparseValue<'I, 'T
            when ^I: equality
            and ^I: comparison
            and ^T: (static member ( * ) : ^T * ^T -> ^T)
            and ^T: (static member ( + ) : ^T * ^T -> ^T)
            and ^T: (static member ( - ) : ^T * ^T -> ^T)
            and ^T: (static member Zero : ^T)
            and ^T: equality
            and ^T: comparison> =
        {
            x : 'I
            value : 'T
        }


    type SparseArray<'I, 'T
            when ^I: equality
            and ^I: comparison
            and ^T: (static member ( * ) : ^T * ^T -> ^T)
            and ^T: (static member ( + ) : ^T * ^T -> ^T)
            and ^T: (static member ( - ) : ^T * ^T -> ^T)
            and ^T: (static member Zero : ^T)
            and ^T: equality
            and ^T: comparison> =
        {
            values : SparseValue<'I, 'T>[]
            map : Lazy<Map<'I, 'T>>
        }

        member inline r.total() = r.values |> Seq.ofArray |> Seq.map _.value |> Seq.sum

        static member inline private createLookupMap (values: SparseValue<'I, 'T>[]) =
            values
            |> Array.map (fun v -> v.x, v.value)
            |> Map.ofArray

        member inline a.tryFind i = a.map.Value.TryFind i
        member inline a.getValues() = a.values |> Seq.ofArray

        static member inline create v =
            {
                values = v
                map = new Lazy<Map<'I, 'T>>(fun () -> SparseArray.createLookupMap v)
            }

        static member inline createAbove (z : ZeroThreshold<'T>) (v : 'T[]) =
            v
            |> Array.mapi (fun i e -> if e >= z.value then Some { x = i; value = e } else None)
            |> Array.choose id
            |> SparseArray.create

        static member inline (*) (a : SparseArray<'I, 'U>, b : 'U) : SparseArray<'I, 'U> =
            a.values |> Array.map (fun e -> e * b) |> SparseArray.create

        static member inline (*) (a : 'U, b : SparseArray<'I, 'U>) = b * a

        static member inline (*) (a : SparseArray<'I, 'T>, b : SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
            failwith ""


    /// Extract the key/value pairs from the dictionary into an SparseArray.
    let inline private toSparseArray (dict : Dictionary<'I, 'T>) =
        let resultArray =
            dict
            |> Seq.map (fun kvp -> { x = kvp.Key; value = kvp.Value })
            |> Seq.toArray

        resultArray |> SparseArray.create


    let inline sumSparseArrays (arrays: list<SparseArray<'I, 'T>>) : SparseArray<'I, 'T> =
        let dict = Dictionary<'I, 'T>()

        for values in arrays do
            for v in values.getValues() do
                let key = v.x

                if dict.ContainsKey(key) then dict.[key] <- dict.[key] + v.value
                else dict.Add(key, v.value)

        // A sum is generally inseparable.
        toSparseArray dict


    let inline multiplySparseArrays (arrays: list<SparseArray<'I, 'T>>) : SparseArray<'I, 'T> =
        let result = Dictionary<'I, 'T>()
        let keysToRemove = HashSet<'I>()

        let g (a : SparseArray<'I, 'T>) =
            keysToRemove.Clear()

            for key in result.Keys do
                match a.tryFind key with
                | Some value -> result.[key] <- result.[key] * value
                | None -> keysToRemove.Add key |> ignore

            for key in keysToRemove do result.Remove(key) |> ignore

        match arrays with
        | [] -> ()
        | h :: t ->
            for v in h.getValues() do result.Add(v.x, v.value)
            t |> List.map g |> ignore

        toSparseArray result


    /// A sparse matrix representation.
    /// A sparse matrix is coded as two functions that return a sparse array for a given x or y.
    /// This is done because the matrices that we are after are insanely huge, and we absolutely cannot store them in memory.
    /// The  largest that we've run a matrix * vector multiplication test was about 3,906,250,000 x 3,906,250,000
    /// for the total of about 1.5E+19 elements in size.
    ///
    /// The caller is responsible for providing the correct functions that return the correct sparse arrays.
    /// One is not enough as recalculating the other is very time-consuming and likely impossible for very large matrices.
    type SparseMatrix<'I, 'T
            when ^I: equality
            and ^I: comparison
            and ^T: (static member ( * ) : ^T * ^T -> ^T)
            and ^T: (static member ( + ) : ^T * ^T -> ^T)
            and ^T: (static member ( - ) : ^T * ^T -> ^T)
            and ^T: (static member Zero : ^T)
            and ^T: equality
            and ^T: comparison> =
        {
            x_y : 'I -> SparseArray<'I, 'T> // Returns a sparse array for the given x.
            y_x : 'I -> SparseArray<'I, 'T> // Returns a sparse array for the given y.
        }


        /// Multiply a SparseMatrix by a SparseArray
        static member inline (*) (matrix : SparseMatrix<'I, 'T>, array : SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
            let result = Dictionary<'I, 'T>()

            // For each point y in the sparse array
            for sparseValue in array.getValues() do
                let y = sparseValue.x
                let y_value = sparseValue.value

                // Get all applicable sparse arrays in the matrix for this y
                let x_values = matrix.y_x y

                // For each x-value in the matrix that corresponds to this y
                for x_sparse_value in x_values.getValues() do
                    let x = x_sparse_value.x
                    let matrix_value = x_sparse_value.value

                    // Calculate multiplication
                    let product = matrix_value * y_value

                    // Add to result
                    if result.ContainsKey(x) then
                        result.[x] <- result.[x] + product
                    else
                        result.Add(x, product)

            // Convert the result dictionary to a SparseArray
            result
            |> Seq.map (fun kvp -> { x = kvp.Key; value = kvp.Value })
            |> Seq.toArray
            |> SparseArray.create

        /// Multiply a SparseArray by a SparseMatrix (reverse order)
        static member inline (*) (array : SparseArray<'I, 'T>, matrix : SparseMatrix<'I, 'T>) : SparseArray<'I, 'T> =
            let result = Dictionary<'I, 'T>()

            // For each point x in the sparse array
            for sparseValue in array.getValues() do
                let x = sparseValue.x
                let x_value = sparseValue.value

                // Get all applicable sparse arrays in the matrix for this x
                let y_values = matrix.x_y x

                // For each y-value in the matrix that corresponds to this x
                for y_sparse_value in y_values.getValues() do
                    let y = y_sparse_value.x
                    let matrix_value = y_sparse_value.value

                    // Calculate multiplication
                    let product = x_value * matrix_value

                    // Add to result
                    if result.ContainsKey(y) then
                        result.[y] <- result.[y] + product
                    else
                        result.Add(y, product)

            // Convert the result dictionary to a SparseArray
            result
            |> Seq.map (fun kvp -> { x = kvp.Key; value = kvp.Value })
            |> Seq.toArray
            |> SparseArray.create


    /// A one-dimensional sparse probability distribution.
    type SparseProbability =
        | SparseProbability of SparseArray<int, double>

        member r.value = let (SparseProbability v) = r in v

        /// TODO kk:20250309 - If integration over multidimensional space is used then the edges and corners acquire
        ///     different coefficients. This is not used here because integration is not performed and we use
        ///     this probability for Poisson evolution. Revisit if this changes.
        ///
        /// Creates a normalized 1D probability. Parameter i is an index in the domain.
        static member create (data : ProbabilityParams) (i : int) =
            let domain = data.domainParams.domain()
            let ef = data.epsFuncValue.epsFunc domain
            let epsFunc i1 = (ef.invoke domain domain.points.value[i1]) * ( double domain.points.value.Length) / 2.0

            let f i1 =
                let v = exp (- pown ((double (i1 - i)) / (epsFunc i1)) 2)
                if v >= data.zeroThreshold.value then Some { x = i1; value = v } else None

            let g i1 =
                match data.maxIndexDiff with
                | Some v -> if abs(i1 - i) > v then None else f i1
                | None -> f i1

            let values =
                domain.points.value
                |> Array.mapi (fun i1 _ -> g i1)
                |> Array.choose id
                |> SparseArray<int, double>.create

            let norm = values.total()
            let p = values.values |> Array.map (fun v -> { v with value = v.value / norm }) |> SparseArray<int, double>.create
            SparseProbability p


    /// A separable multidimensional sparse probability distribution.
    type MultiDimensionalSparseProbability =
        | MultiDimensionalSparseProbability of SparseProbability[]


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
        let y_x (point : Point3D) =
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

        // x_y function: essentially transposition of y_x
        // For random walk matrices, x_y should be the same as y_x since
        // our normalization ensures that each row sums to 1
        { x_y = y_x; y_x = y_x }
