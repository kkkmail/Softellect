namespace Softellect.Math

open System.Collections.Generic
open FSharp.Collections
open System

module Sparse2 =
    /// Arithmetic operations record that encapsulates all required operations
    type ArithmeticOperations<'T> =
        {
            add: 'T -> 'T -> 'T
            subtract: 'T -> 'T -> 'T
            multiply: 'T -> 'T -> 'T
            multiplyByDouble: double -> 'T -> 'T
            divide: 'T -> 'T -> 'T  // Renamed from divideByScalar
            toDouble: 'T -> double  // Convert to double
            zero: 'T
            one: 'T
            isZero: 'T -> bool  // Used for filtering zero values
            greaterThan: 'T -> 'T -> bool  // For comparison operations
        }

    /// Representation of non-zero value in an abstract sparse array
    type SparseValue<'I, 'T when 'I : comparison and 'T : equality and 'T : comparison> =
        {
            x : 'I
            value : 'T
        }

    /// Sparse array implementation
    type SparseArray<'I, 'T when 'I : comparison and 'T : equality and 'T : comparison> =
        {
            values : SparseValue<'I, 'T>[]
            map : Lazy<Map<'I, 'T>>
        }

    /// A sparse matrix representation
    type SparseMatrix<'I, 'T when 'I : comparison and 'T : equality and 'T : comparison> =
        {
            x_y : 'I -> SparseArray<'I, 'T> // Returns a sparse array for the given x
            y_x : 'I -> SparseArray<'I, 'T> // Returns a sparse array for the given y
        }

    // SparseValue operations
    let convertValue (converter: 'T -> 'U) (v: SparseValue<'I, 'T>) : SparseValue<'I, 'U> =
        {
            x = v.x
            value = converter v.value
        }

    // SparseArray core operations
    let private createLookupMap (values: SparseValue<'I, 'T>[]) =
        values
        |> Array.map (fun v -> v.x, v.value)
        |> Map.ofArray

    let create (arithmetic: ArithmeticOperations<'T>) (v: SparseValue<'I, 'T>[]) =
        let values = v |> Array.filter (fun e -> not (arithmetic.isZero e.value))

        {
            values = values
            map = Lazy<Map<'I, 'T>>(fun () -> createLookupMap values)
        }

    let empty<'I, 'T when 'I : comparison and 'T : equality and 'T : comparison> () : SparseArray<'I, 'T> =
        {
            values = [||]
            map = Lazy<Map<'I, 'T>>(fun () -> Map.empty)
        }

    // SparseArray utility functions
    let tryFind (array: SparseArray<'I, 'T>) (i: 'I) =
        array.map.Value.TryFind i

    let getValues (array: SparseArray<'I, 'T>) =
        array.values |> Seq.ofArray

    // Arithmetic operations
    let total (arithmetic: ArithmeticOperations<'T>) (array: SparseArray<'I, 'T>) =
        array.values
        |> Array.fold (fun acc v -> arithmetic.add acc v.value) arithmetic.zero

    let convert (arithmetic: ArithmeticOperations<'U>) (converter: 'T -> 'U) (array: SparseArray<'I, 'T>) =
        array.values
        |> Array.map (convertValue converter)
        |> create arithmetic

    let createAbove (arithmetic: ArithmeticOperations<'T>) (threshold: 'T) (v: 'T[]) =
        v
        |> Array.mapi (fun i e ->
            if not (arithmetic.isZero e) && arithmetic.greaterThan e threshold
            then Some { x = i; value = e }
            else None)
        |> Array.choose id
        |> create arithmetic

    // Moment calculations - corrected implementation
    let moment
        (vArithmetic: ArithmeticOperations<'V>)
        (cArithmetic: ArithmeticOperations<'C>)
        (converter: 'T -> 'V)
        (projector: 'I -> 'C)
        (n: int)
        (array: SparseArray<'I, 'T>) : 'C =

        let converted = array.values |> Array.map (convertValue converter)
        let x0 = converted |> Array.fold (fun acc v -> vArithmetic.add acc v.value) vArithmetic.zero

        if vArithmetic.greaterThan x0 vArithmetic.zero then
            let xn =
                converted
                |> Array.fold (fun acc v ->
                    let proj = projector v.x
                    let powed =
                        [1..n-1]
                        |> List.fold (fun p _ -> cArithmetic.multiply p proj) proj

                    // Use vArithmetic.toDouble to get the scalar value
                    let scalar = vArithmetic.toDouble v.value
                    let term = cArithmetic.multiplyByDouble scalar powed
                    cArithmetic.add acc term
                ) cArithmetic.zero

            // Divide xn by x0 to get the moment
            let x0Double = vArithmetic.toDouble x0
            cArithmetic.multiplyByDouble (1.0 / x0Double) xn
        else
            cArithmetic.zero

    let mean (vArithmetic: ArithmeticOperations<'V>) (cArithmetic: ArithmeticOperations<'C>) (converter: 'T -> 'V) (projector: 'I -> 'C) (array: SparseArray<'I, 'T>) =
        moment vArithmetic cArithmetic converter projector 1 array

    let variance (vArithmetic: ArithmeticOperations<'V>) (cArithmetic: ArithmeticOperations<'C>) (converter: 'T -> 'V) (projector: 'I -> 'C) (array: SparseArray<'I, 'T>) =
        let m1 = moment vArithmetic cArithmetic converter projector 1 array
        let m2 = moment vArithmetic cArithmetic converter projector 2 array
        let m1Squared = cArithmetic.multiply m1 m1
        cArithmetic.subtract m2 m1Squared

    // SparseArray operators implemented as functions
    let multiplyByScalar (arithmetic: ArithmeticOperations<'T>) (array: SparseArray<'I, 'T>) (scalar: 'T) =
        array.values
        |> Array.map (fun e ->
            {
                x = e.x
                value = arithmetic.multiply e.value scalar
            })
        |> create arithmetic

    // Convert a dictionary to SparseArray
    let private toSparseArray (arithmetic: ArithmeticOperations<'T>) (dict: Dictionary<'I, 'T>) =
        dict
        |> Seq.map (fun kvp ->
            {
                x = kvp.Key
                value = kvp.Value
            })
        |> Seq.toArray
        |> create arithmetic

    // Sum operations
    let sumSparseArrays (arithmetic: ArithmeticOperations<'T>) (arrays: list<SparseArray<'I, 'T>>) : SparseArray<'I, 'T> =
        let dict = Dictionary<'I, 'T>()

        for array in arrays do
            for v in getValues array do
                let key = v.x

                if dict.ContainsKey(key) then
                    dict[key] <- arithmetic.add dict[key] v.value
                else
                    dict.Add(key, v.value)

        toSparseArray arithmetic dict

    // Multiply operations
    let multiplySparseArrays (arithmetic: ArithmeticOperations<'T>) (arrays: list<SparseArray<'I, 'T>>) : SparseArray<'I, 'T> =
        let result = Dictionary<'I, 'T>()
        let keysToRemove = HashSet<'I>()

        let processArray (a: SparseArray<'I, 'T>) =
            keysToRemove.Clear()

            for key in result.Keys do
                match tryFind a key with
                | Some value -> result[key] <- arithmetic.multiply result[key] value
                | None -> keysToRemove.Add key |> ignore

            for key in keysToRemove do result.Remove(key) |> ignore

        match arrays with
        | [] -> ()
        | h :: t ->
            for v in getValues h do result.Add(v.x, v.value)
            t |> List.iter processArray

        toSparseArray arithmetic result

    // Add two SparseArrays
    let add (arithmetic: ArithmeticOperations<'T>) (a: SparseArray<'I, 'T>) (b: SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
        sumSparseArrays arithmetic [a; b]

    // Multiply two SparseArrays (elementwise)
    let multiply (arithmetic: ArithmeticOperations<'T>) (a: SparseArray<'I, 'T>) (b: SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
        multiplySparseArrays arithmetic [a; b]

    // Subtract two SparseArrays
    let subtract (arithmetic: ArithmeticOperations<'T>) (a: SparseArray<'I, 'T>) (b: SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
        let dict = Dictionary<'I, 'T>()

        for v in getValues a do
            dict.Add(v.x, v.value)

        for v in getValues b do
            let key = v.x

            if dict.ContainsKey(key) then
                dict[key] <- arithmetic.subtract dict[key] v.value
            else
                dict.Add(key, arithmetic.subtract arithmetic.zero v.value)

        toSparseArray arithmetic dict

    // SparseMatrix operations
    let emptyMatrix<'I, 'T when 'I : comparison and 'T : equality and 'T : comparison> () : SparseMatrix<'I, 'T> =
        {
            x_y = fun _ -> empty()
            y_x = fun _ -> empty()
        }

    // Matrix-vector multiplication
    let multiplyMatrixVector (arithmetic: ArithmeticOperations<'T>) (matrix: SparseMatrix<'I, 'T>) (array: SparseArray<'I, 'T>) : SparseArray<'I, 'T> =
        let result = Dictionary<'I, 'T>()

        // For each point y in the sparse array
        for sparseValue in getValues array do
            let y = sparseValue.x
            let y_value = sparseValue.value

            // Get all applicable sparse arrays in the matrix for this y
            let x_values = matrix.y_x y

            // For each x-value in the matrix that corresponds to this y
            for x_sparse_value in getValues x_values do
                let x = x_sparse_value.x
                let matrix_value = x_sparse_value.value

                // Calculate multiplication
                let product = arithmetic.multiply matrix_value y_value

                // Add to result
                if result.ContainsKey(x) then
                    result[x] <- arithmetic.add result[x] product
                else
                    result.Add(x, product)

        toSparseArray arithmetic result

    // Vector-matrix multiplication
    let multiplyVectorMatrix (arithmetic: ArithmeticOperations<'T>) (array: SparseArray<'I, 'T>) (matrix: SparseMatrix<'I, 'T>) : SparseArray<'I, 'T> =
        let result = Dictionary<'I, 'T>()

        // For each point x in the sparse array
        for sparseValue in getValues array do
            let x = sparseValue.x
            let x_value = sparseValue.value

            // Get all applicable sparse arrays in the matrix for this x
            let y_values = matrix.x_y x

            // For each y-value in the matrix that corresponds to this x
            for y_sparse_value in getValues y_values do
                let y = y_sparse_value.x
                let matrix_value = y_sparse_value.value

                // Calculate multiplication
                let product = arithmetic.multiply x_value matrix_value

                // Add to result
                if result.ContainsKey(y) then
                    result[y] <- arithmetic.add result[y] product
                else
                    result.Add(y, product)

        toSparseArray arithmetic result

    // Standard arithmetic implementations
    module Standard =
        let floatArithmetic =
            {
                add = (+)
                subtract = (-)
                multiply = (*)
                multiplyByDouble = (fun d t -> d * t)
                divide = (/)
                toDouble = double
                zero = 0.0
                one = 1.0
                isZero = (fun x -> abs x < 1e-10)  // Using epsilon for floating point
                greaterThan = (>)
            }

        let intArithmetic =
            {
                add = (+)
                subtract = (-)
                multiply = (*)
                multiplyByDouble = (fun d t -> int (d * double t))
                divide = (/)
                toDouble = double
                zero = 0
                one = 1
                isZero = (fun x -> x = 0)
                greaterThan = (>)
            }

        let int64Arithmetic =
            {
                add = (+)
                subtract = (-)
                multiply = (*)
                multiplyByDouble = (fun d t -> int64 (d * double t))
                divide = (/)
                toDouble = double
                zero = 0L
                one = 1L
                isZero = (fun x -> x = 0L)
                greaterThan = (>)
            }
