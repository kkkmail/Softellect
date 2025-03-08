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

        // static member inline createAbove (z : ZeroThreshold<'T>) (v : 'T[]) =
        //     let x =
        //         v
        //         |> Array.mapi (fun i e -> if e >= z.value then Some { i = i; value1D = e } else None)
        //         |> Array.choose id
        //     {
        //         xValues = x
        //         xMap = new Lazy<Map<int, 'T>>(fun () -> SparseArray.createLookupMap x)
        //     }

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


    /// A Sparse matrix representation.
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
