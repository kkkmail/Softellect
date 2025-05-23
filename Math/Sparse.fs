﻿namespace Softellect.Math

open System.Collections.Generic
open FSharp.Collections
open System
open MBrace.FsPickler
open Softellect.Math.Primitives

module Sparse =

    /// Representation of non-zero value in an abstract sparse array
    type SparseValue<'I, 'T
            when ^I: equality
            and ^I: comparison

            and ^T: equality
            and ^T: comparison
            and ^T: (static member ( * ) : ^T * ^T -> ^T)
            and ^T: (static member ( + ) : ^T * ^T -> ^T)
            and ^T: (static member ( - ) : ^T * ^T -> ^T)
            and ^T: (static member Zero : ^T)> =
        {
            x : 'I
            value : 'T
        }

        member inline r.convert converter = { x = r.x; value = converter r.value }
        member inline r.project projector = { x = projector r.x; value = r.value }


    let inline private createLookupMap (values: SparseValue<'I, 'T>[]) =
        values
        |> Array.map (fun v -> v.x, v.value)
        |> Map.ofArray


    /// A sparse array implementation
    [<CustomPickler>]
    type SparseArray<'I, 'T
            when ^I: equality
            and ^I: comparison

            and ^T: equality
            and ^T: comparison
            and ^T: (static member ( * ) : ^T * ^T -> ^T)
            and ^T: (static member ( + ) : ^T * ^T -> ^T)
            and ^T: (static member ( - ) : ^T * ^T -> ^T)
            and ^T: (static member op_Explicit : ^T -> double)
            and ^T: (static member Zero : ^T)> =
        {
            values : SparseValue<'I, 'T>[]
            map : Lazy<Map<'I, 'T>>
        }

        static member inline create (v: SparseValue<'I, 'T>[]) =
            // Remove all zero values.
            let values = v |> Array.filter (fun e -> e.value <> LanguagePrimitives.GenericZero)

            {
                values = values
                map = Lazy<Map<'I, 'T>>(fun () -> createLookupMap values)
            }

        static member inline CreatePickler(resolver : IPicklerResolver) =
            let name = nameof(SparseArray)
            let pickler = resolver.Resolve<SparseValue<'I, 'T>[]> ()
            let writer (w : WriteState) (a : SparseArray<'I, 'T>) = pickler.Write w name a.values
            let reader (r : ReadState) = let v = pickler.Read r name in ( SparseArray<'I, 'T>.create v)
            Pickler.FromPrimitives(reader, writer)

        static member inline empty() : SparseArray<'I, 'T> =
            {
                values = [||]
                map = Lazy<Map<'I, 'T>>(fun () -> Map.empty)
            }

        member inline array.tryFind (i: 'I) = array.map.Value.TryFind i
        member inline array.getValues()  = array.values |> Seq.ofArray
        member inline array.total() = array.values |> Array.sumBy _.value

        member inline array.convert converter =
            array.values
            |> Array.map (fun e -> e.convert converter)
            |> SparseArray.create

        member inline array.project projector =
            array.values
            |> Array.map (fun e -> e.project projector)
            |> SparseArray.create

        /// Calculates diagonal moment of the array.
        member inline array.moment (parameters: ConversionParameters<'I, 'C>) n =
            let c = array.values |> Array.map (fun v -> v.convert double)
            let x0 = c |> Array.sumBy _.value

            let pown x n =
                let v = parameters.projector x
                let mutable result = parameters.arithmetic.one
                for _ in 1..n do result <- parameters.arithmetic.multiply result v
                result

            if x0 > 0.0
            then
                let xn =
                    c
                    |> Array.map (fun v -> parameters.arithmetic.multiplyByDouble v.value (pown v.x n))
                    |> Array.fold parameters.arithmetic.add parameters.arithmetic.zero

                parameters.arithmetic.multiplyByDouble (1.0 / x0) xn
            else parameters.arithmetic.zero

        member inline array.mean parameters = array.moment parameters 1

        member inline array.variance (parameters: ConversionParameters<'I, 'C>) =
            let m1 = array.moment parameters 1
            let m2 = array.moment parameters 2
            let m1Squared = parameters.arithmetic.multiply m1 m1
            parameters.arithmetic.subtract m2 m1Squared

        member inline array.variance (df : DistanceFunction<'I, double>) : double =
            let values =
                array.values
                |> Array.map (fun v -> v.convert double)

            let x0 = values |> Array.sumBy _.value

            if x0 > 0.0
            then
                let d a b =
                    let x = df.invoke a.x b.x
                    x * x * a.value * b.value

                let v =
                    values
                    |> Array.map (fun a -> values |> Array.map (d a) |> Array.sum)
                    |> Array.sum

                v / (2.0 * x0 * x0)
            else 0.0

        // Calculate 1st order tensor moment
        member inline array.tensorMoment1 (parameters: ConversionParameters<'I, 'C>) =
            match parameters.arithmetic.toArray with
            | Some toArrayFn ->
                let c = array.values |> Array.map (fun v -> v.convert double)
                let x0 = c |> Array.sumBy _.value
                let dims = (toArrayFn parameters.arithmetic.one).Length
                let tensor = Array.create dims 0.0

                if x0 > 0.0 then
                    for point in c do
                        let v = toArrayFn (parameters.projector point.x)
                        for i in 0..dims-1 do tensor[i] <- tensor[i] + point.value * v[i]

                    for i in 0..dims-1 do tensor[i] <- tensor[i] / x0
                Some tensor
            | None -> None

        // Calculate 2nd order tensor moment (returns a 2D jagged array)
        member inline array.tensorMoment2 (parameters: ConversionParameters<'I, 'C>) =
            match parameters.arithmetic.toArray with
            | Some toArrayFn ->
                let c = array.values |> Array.map (fun v -> v.convert double)
                let x0 = c |> Array.sumBy _.value
                let dims = (toArrayFn parameters.arithmetic.one).Length
                let tensor = Array.init dims (fun _ -> Array.create dims 0.0)

                if x0 > 0.0 then
                    for point in c do
                        let v = toArrayFn (parameters.projector point.x)
                        for i in 0..dims-1 do for j in 0..dims-1 do tensor[i][j] <- tensor[i][j] + point.value * v[i] * v[j]

                    for i in 0..dims-1 do for j in 0..dims-1 do tensor[i][j] <- tensor[i][j] / x0
                Some tensor
            | None -> None

        member inline array.tensorVariance (parameters: ConversionParameters<'I, 'C>) =
            match array.tensorMoment1 parameters, array.tensorMoment2 parameters with
            | Some m1, Some m2 ->
                let dims = m1.Length
                for i in 0..dims-1 do for j in 0..dims-1 do m2[i][j] <- m2[i][j] - m1[i] * m1[j]
                Some m2
            | _ -> None

        // Calculate 3rd order tensor moment (returns a 3D jagged array)
        member inline array.tensorMoment3 (parameters: ConversionParameters<'I, 'C>) =
            match parameters.arithmetic.toArray with
            | Some toArrayFn ->
                let c = array.values |> Array.map (fun v -> v.convert double)
                let x0 = c |> Array.sumBy _.value
                let dims = (toArrayFn parameters.arithmetic.one).Length
                let tensor = Array.init dims (fun _ -> Array.init dims (fun _ -> Array.create dims 0.0))

                if x0 > 0.0 then
                    for point in c do
                        let v = toArrayFn (parameters.projector point.x)
                        for i in 0..dims-1 do for j in 0..dims-1 do for k in 0..dims-1 do tensor.[i].[j].[k] <- tensor.[i].[j].[k] + point.value * v.[i] * v.[j] * v.[k]

                    for i in 0..dims-1 do for j in 0..dims-1 do for k in 0..dims-1 do tensor.[i].[j].[k] <- tensor.[i].[j].[k] / x0

                Some tensor
            | None -> None


        static member inline internal toSparseArray (dict: Dictionary<'I, 'T>) =
            dict
            |> Seq.map (fun kvp -> { x = kvp.Key; value = kvp.Value })
            |> Seq.toArray
            |> SparseArray.create

        static member inline sum  (arrays: list<SparseArray<'I, 'T>>) =
            let dict = Dictionary<'I, 'T>()

            for array in arrays do
                for v in array.getValues() do
                    let key = v.x

                    if dict.ContainsKey(key) then dict[key] <- dict[key] + v.value
                    else dict.Add(key, v.value)

            SparseArray.toSparseArray dict

        static member inline multiply (arrays: list<SparseArray<'I, 'T>>) =
            let result = Dictionary<'I, 'T>()
            let keysToRemove = HashSet<'I>()

            let processArray (a: SparseArray<'I, 'T>) =
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
                t |> List.iter processArray

            SparseArray.toSparseArray result

        member inline array.add b = SparseArray.sum [array; b]

        member inline array.subtract (b: SparseArray<'I, 'T>) =
            let dict = Dictionary<'I, 'T>()

            for v in array.getValues() do dict.Add(v.x, v.value)

            for v in b.getValues() do
                let key = v.x

                if dict.ContainsKey(key) then dict[key] <- dict[key] - v.value
                else dict.Add(key, LanguagePrimitives.GenericZero - v.value)

            SparseArray.toSparseArray dict


    /// A sparse matrix representation
    type SparseMatrix<'I, 'T
            when ^I: equality
            and ^I: comparison

            and ^T: equality
            and ^T: comparison
            and ^T: (static member ( * ) : ^T * ^T -> ^T)
            and ^T: (static member ( + ) : ^T * ^T -> ^T)
            and ^T: (static member ( - ) : ^T * ^T -> ^T)
            and ^T: (static member op_Explicit : ^T -> double)
            and ^T: (static member Zero : ^T)> =
        {
            x_y : 'I -> SparseArray<'I, 'T> // Returns a sparse array for the given x
            y_x : 'I -> SparseArray<'I, 'T> // Returns a sparse array for the given y
        }

        static member inline empty() : SparseMatrix<'I, 'T> =
            {
                x_y = fun _ -> SparseArray.empty()
                y_x = fun _ -> SparseArray.empty()
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

                    let product = matrix_value * y_value
                    if result.ContainsKey(x) then result[x] <- result[x] + product
                    else result.Add(x, product)

            SparseArray.toSparseArray result

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

                    let product = x_value * matrix_value
                    if result.ContainsKey(y) then result[y] <- result[y] + product
                    else result.Add(y, product)

            SparseArray.toSparseArray result


    // let private createAbove (arithmetic: ArithmeticOperations<'T>) (threshold: 'T) (v: 'T[]) =
    //     v
    //     |> Array.mapi (fun i e ->
    //         if not (arithmetic.zero <> e) &&  e > threshold
    //         then Some { x = i; value = e }
    //         else None)
    //     |> Array.choose id
    //     |> create arithmetic


    // let private multiplyByScalar (arithmetic: ArithmeticOperations<'T>) (array: SparseArray<'I, 'T>) (scalar: 'T) =
    //     array.values
    //     |> Array.map (fun e ->
    //         {
    //             x = e.x
    //             value = arithmetic.multiply e.value scalar
    //         })
    //     |> SparseArray.create arithmetic





    // let private multiply (arithmetic: ArithmeticOperations<'T>) (a: SparseArray<'I, 'T>) (b: SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
    //     multiplySparseArrays arithmetic [a; b]
