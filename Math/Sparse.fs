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

        member inline r.convert converter = { x = r.x; value = converter r.value }


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
            // Remove all zero values.
            let values = v |> Array.filter (fun e -> e.value <> LanguagePrimitives.GenericZero<'T>)

            {
                values = values
                map = new Lazy<Map<'I, 'T>>(fun () -> SparseArray.createLookupMap values)
            }

        static member inline empty = SparseArray<'I, 'T>.create [||]

        static member inline createAbove (z : ZeroThreshold<'T>) (v : 'T[]) =
            v
            |> Array.mapi (fun i e -> if e >= z.value then Some { x = i; value = e } else None)
            |> Array.choose id
            |> SparseArray.create

        member inline r.convert converter =
            r.values |> Array.map (fun v -> v.convert converter) |> SparseArray.create

        member inline r.moment (converter : ^T -> ^V) (projector : ^I -> ^C ) (n : int) : ^C =
            let c = r.values |> Array.map (fun v -> v.convert converter)
            let x0 = c |> Array.sumBy _.value

            if x0 > LanguagePrimitives.GenericZero<'V>
            then
                let xn =
                    c
                    |> Array.map (fun v -> (pown (projector v.x) n) *. v.value)
                    |> Array.sum

                xn /. x0
            else LanguagePrimitives.GenericZero<'C>

        member inline r.mean (converter : ^T -> ^V) (projector : ^I -> ^C ) : ^C =
            let m1 = r.moment converter projector 1
            m1

        member inline r.variance (converter : ^T -> ^V) (projector : ^I -> ^C ) : ^C =
            let m1 = r.moment converter projector 1
            let m2 = r.moment converter projector 2
            let variance = m2 - (m1 * m1)
            variance

        static member inline (*) (a : SparseArray<'I, 'U>, b : 'U) : SparseArray<'I, 'U> =
            a.values |> Array.map (fun e -> e * b) |> SparseArray.create

        static member inline (*) (a : 'U, b : SparseArray<'I, 'U>) = b * a


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

                if dict.ContainsKey(key) then dict[key] <- dict[key] + v.value
                else dict.Add(key, v.value)

        toSparseArray dict


    let inline multiplySparseArrays (arrays: list<SparseArray<'I, 'T>>) : SparseArray<'I, 'T> =
        let result = Dictionary<'I, 'T>()
        let keysToRemove = HashSet<'I>()

        let g (a : SparseArray<'I, 'T>) =
            keysToRemove.Clear()

            for key in result.Keys do
                match a.tryFind key with
                | Some value -> result[key] <- result[key] * value
                | None -> keysToRemove.Add key |> ignore

            for key in keysToRemove do result.Remove(key) |> ignore

        match arrays with
        | [] -> ()
        | h :: t ->
            for v in h.getValues() do result.Add(v.x, v.value)
            t |> List.map g |> ignore

        toSparseArray result


    type SparseArray<'I, 'T
            when ^I: equality
            and ^I: comparison
            and ^T: (static member ( * ) : ^T * ^T -> ^T)
            and ^T: (static member ( + ) : ^T * ^T -> ^T)
            and ^T: (static member ( - ) : ^T * ^T -> ^T)
            and ^T: (static member Zero : ^T)
            and ^T: equality
            and ^T: comparison>
        with

        static member inline (*) (a : SparseArray<'I, 'T>, b : SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
            [ a; b ] |> multiplySparseArrays

        static member inline (+) (a : SparseArray<'I, 'T>, b : SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
            [ a; b ] |> sumSparseArrays

        static member inline (-) (a : SparseArray<'I, 'T>, b : SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
            let dict = Dictionary<'I, 'T>()

            for v in a.getValues() do
                let key = v.x

                if dict.ContainsKey(key) then dict[key] <- v.value
                else dict.Add(key, v.value)

            for v in b.getValues() do
                let key = v.x

                if dict.ContainsKey(key) then dict[key] <- dict[key] - v.value
                else dict.Add(key, LanguagePrimitives.GenericZero<'T> - v.value)

            toSparseArray dict


    /// A sparse matrix representation.
    ///
    /// A sparse matrix is coded as two functions that return a sparse array for a given x or y.
    /// This is done that way because the matrices that we are after are insanely huge,
    /// and they absolutely cannot be stored in memory, even in sparse form.
    ///
    /// The  largest matrix that we've run a matrix * vector multiplication test
    /// was about 3,906,250,000 x 3,906,250,000 matrix for the total of about 1.5E+19 elements in size.
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
                        result[x] <- result[x] + product
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
                        result[y] <- result[y] + product
                    else
                        result.Add(y, product)

            // Convert the result dictionary to a SparseArray
            result
            |> Seq.map (fun kvp -> { x = kvp.Key; value = kvp.Value })
            |> Seq.toArray
            |> SparseArray.create

        static member inline empty =
            {
                x_y = fun _ -> SparseArray<'I, 'T>.empty
                y_x = fun _ -> SparseArray<'I, 'T>.empty
            }
