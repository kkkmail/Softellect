namespace Softellect.Math

open System.Collections.Generic
open FSharp.Collections
open System
open Softellect.Math.Primitives

module Sparse2 =

    /// Representation of non-zero value in an abstract sparse array
    type SparseValue<'I, 'T when 'I : comparison and 'T : equality and 'T : comparison> =
        {
            x : 'I
            value : 'T
        }

        member r.convert converter = { x = r.x; value = converter r.value }


    let private createLookupMap (values: SparseValue<'I, 'T>[]) =
        values
        |> Array.map (fun v -> v.x, v.value)
        |> Map.ofArray


    /// A sparse array implementation
    type SparseArray<'I, 'T when 'I : comparison and 'T : equality and 'T : comparison> =
        {
            values : SparseValue<'I, 'T>[]
            map : Lazy<Map<'I, 'T>>
        }

        static member create filter (v: SparseValue<'I, 'T>[]) =
            let values = v |> Array.filter (fun e -> filter e.value)

            {
                values = values
                map = Lazy<Map<'I, 'T>>(fun () -> createLookupMap values)
            }

        static member empty() : SparseArray<'I, 'T> =
            {
                values = [||]
                map = Lazy<Map<'I, 'T>>(fun () -> Map.empty)
            }


        member array.tryFind (i: 'I) = array.map.Value.TryFind i
        member array.getValues()  = array.values |> Seq.ofArray

        member array.total arithmetic =
            array.values
            |> Array.fold (fun acc v -> arithmetic.add acc v.value) arithmetic.zero


        member array.convert arithmetic converter =
            array.values
            |> Array.map (fun e -> e.convert converter)
            |> SparseArray.create arithmetic

        member array.moment (parameters: ConversionParameters<'I, 'T, 'C>) n =
            // Sum of all values (as double)
            let mutable x0 = 0.0
            for v in array.values do x0 <- x0 + parameters.converter v.value

            if x0 > 0.0 then
                // Calculate weighted sum
                let mutable xn = parameters.arithmetic.zero

                for v in array.values do
                    let proj = parameters.projector v.x
                    let powed =
                        [1..n-1]
                        |> List.fold (fun p _ -> parameters.arithmetic.multiply p proj) proj

                    let valueDouble = parameters.converter v.value
                    let term = parameters.arithmetic.multiplyByDouble valueDouble powed
                    xn <- parameters.arithmetic.add xn term

                // Divide by sum to get moment
                parameters.arithmetic.multiplyByDouble (1.0 / x0) xn
            else parameters.arithmetic.zero

        member array.mean parameters = array.moment parameters 1

        member array.variance parameters =
            let m1 = array.moment parameters 1
            let m2 = array.moment parameters 2
            let m1Squared = parameters.arithmetic.multiply m1 m1
            parameters.arithmetic.subtract m2 m1Squared

        static member internal toSparseArray filter (dict: Dictionary<'I, 'T>) =
            dict
            |> Seq.map (fun kvp -> { x = kvp.Key; value = kvp.Value })
            |> Seq.toArray
            |> SparseArray.create filter

        static member sum arithmetic (arrays: list<SparseArray<'I, 'T>>) =
            let dict = Dictionary<'I, 'T>()

            for array in arrays do
                for v in array.getValues() do
                    let key = v.x

                    if dict.ContainsKey(key) then dict[key] <- arithmetic.add dict[key] v.value
                    else dict.Add(key, v.value)

            SparseArray.toSparseArray arithmetic.filter dict

        static member multiply arithmetic (arrays: list<SparseArray<'I, 'T>>) =
            let result = Dictionary<'I, 'T>()
            let keysToRemove = HashSet<'I>()

            let processArray (a: SparseArray<'I, 'T>) =
                keysToRemove.Clear()

                for key in result.Keys do
                    match a.tryFind key with
                    | Some value -> result[key] <- arithmetic.multiply result[key] value
                    | None -> keysToRemove.Add key |> ignore

                for key in keysToRemove do result.Remove(key) |> ignore

            match arrays with
            | [] -> ()
            | h :: t ->
                for v in h.getValues() do result.Add(v.x, v.value)
                t |> List.iter processArray

            SparseArray.toSparseArray arithmetic.filter result

        member array.add arithmetic b = SparseArray.sum arithmetic [array; b]

        member array.subtract arithmetic (b: SparseArray<'I, 'T>) =
            let dict = Dictionary<'I, 'T>()

            for v in array.getValues() do dict.Add(v.x, v.value)

            for v in b.getValues() do
                let key = v.x

                if dict.ContainsKey(key) then dict[key] <- arithmetic.subtract dict[key] v.value
                else dict.Add(key, arithmetic.subtract arithmetic.zero v.value)

            SparseArray.toSparseArray arithmetic.filter dict


    /// A sparse matrix representation
    type SparseMatrix<'I, 'T when 'I : comparison and 'T : equality and 'T : comparison> =
        {
            x_y : 'I -> SparseArray<'I, 'T> // Returns a sparse array for the given x
            y_x : 'I -> SparseArray<'I, 'T> // Returns a sparse array for the given y
        }

        static member empty() : SparseMatrix<'I, 'T> =
            {
                x_y = fun _ -> SparseArray.empty()
                y_x = fun _ -> SparseArray.empty()
            }

        member matrix.multiply arithmetic (array: SparseArray<'I, 'T>) =
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

                    let product = arithmetic.multiply matrix_value y_value
                    if result.ContainsKey(x) then result[x] <- arithmetic.add result[x] product
                    else result.Add(x, product)

            SparseArray.toSparseArray arithmetic.filter result

        member matrix.leftMultiply arithmetic (array: SparseArray<'I, 'T>) =
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

                    let product = arithmetic.multiply x_value matrix_value
                    if result.ContainsKey(y) then result[y] <- arithmetic.add result[y] product
                    else result.Add(y, product)

            SparseArray.toSparseArray arithmetic.filter result


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


    // // Standard arithmetic implementations
    // module Standard =
    //     let floatArithmetic =
    //         {
    //             add = (+)
    //             subtract = (-)
    //             multiply = (*)
    //             multiplyByDouble = (fun d t -> d * t)
    //             divide = (/)
    //             toDouble = double
    //             zero = 0.0
    //             one = 1.0
    //             isZero = (fun x -> abs x < 1e-10)  // Using epsilon for floating point
    //             greaterThan = (>)
    //         }
    //
    //     let intArithmetic =
    //         {
    //             add = (+)
    //             subtract = (-)
    //             multiply = (*)
    //             multiplyByDouble = (fun d t -> int (d * double t))
    //             divide = (/)
    //             toDouble = double
    //             zero = 0
    //             one = 1
    //             isZero = (fun x -> x = 0)
    //             greaterThan = (>)
    //         }
    //
    //     let int64Arithmetic =
    //         {
    //             add = (+)
    //             subtract = (-)
    //             multiply = (*)
    //             multiplyByDouble = (fun d t -> int64 (d * double t))
    //             divide = (/)
    //             toDouble = double
    //             zero = 0L
    //             one = 1L
    //             isZero = (fun x -> x = 0L)
    //             greaterThan = (>)
    //         }
