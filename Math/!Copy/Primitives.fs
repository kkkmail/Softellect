namespace Softellect.Math

open FSharp.Collections
open MathNet.Numerics.Distributions
open System
// open Primitives.VersionInfo
open Softellect.Sys.Primitives
open Softellect.Sys.Core

module Primitives =

    // [<Literal>]
    // let DefaultRootFolder = DefaultRootDrive + @":\" + ContGenBaseName + @"\Clm\"
    //
    // [<Literal>]
    // let DefaultResultLocationFolder = DefaultRootFolder + "Results"
    //
    // [<Literal>]
    // let DefaultFileStorageFolder = DefaultRootFolder + "FileStorage"
    //
    // let bindPrefix p v = v |> Option.bind (fun e -> Some $"{p}{e}")
    //
    //
    // let toDoubleString (actualValue: double) : string =
    //     let actualValDec = decimal actualValue
    //     let roundedActualVal = Decimal.Round(actualValDec, 10)
    //
    //     // Local function to extract decimal part from a decimal number.
    //     let extractDecimalPart (decValue: decimal) =
    //         if decValue = 0.0M then "0"
    //         else
    //             let str = string decValue
    //             str.Split('.').[1]
    //
    //     if roundedActualVal < 1.0M then extractDecimalPart roundedActualVal
    //     else
    //         // If actualValue is greater than or equal to 1.
    //         let wholePart = int roundedActualVal
    //         let decimalPartStr = extractDecimalPart (roundedActualVal - decimal wholePart)
    //         $"%d{wholePart}_%s{decimalPartStr}"
    //
    //
    // /// Return an optional string to be used in model name generation.
    // let toModelString (defaultValue: double) (actualValue: double) : string option =
    //     let epsilon = 1e-10M // tolerance for comparing decimals
    //
    //     // Convert doubles to decimals and round to eliminate noise.
    //     let defaultValDec = decimal defaultValue
    //     let actualValDec = decimal actualValue
    //     let roundedActualVal = Decimal.Round(actualValDec, 10)
    //
    //     // Check if the values are effectively the same.
    //     if Math.Abs (defaultValDec - roundedActualVal) < epsilon then None
    //     else toDoubleString actualValue |> Some
    //
    //
    // let private powers = [ ("K", 1_000L); ("M", 1_000_000L); ("G", 1_000_000_000L); ("T", 1_000_000_000_000L); ("P", 1_000_000_000_000_000L); ("E", 1_000_000_000_000_000_000L) ]
    //
    //
    // /// Return an optional string to be used in model name generation.
    // let toModelStringInt64 (defaultValue: int64) (actualValue: int64) : string option =
    //     if defaultValue = actualValue then None
    //     else
    //         let suffix, power = powers |> List.find (fun (_, power) -> (actualValue / power) / 1000L = 0L)
    //         let adjustedValue = double actualValue / double power
    //         let formattedString = toDoubleString adjustedValue
    //
    //         if formattedString.EndsWith("_0")
    //         then formattedString.Replace("_0", suffix)
    //         else formattedString.Replace("_", suffix)
    //         |> Some
    //
    //
    // /// Return an optional string to be used in model name generation.
    // let toModelStringArray (defaultValues: double array) (actualValues: double array) : string option =
    //     let separator = "#"
    //     let pairOptionToStr opt = opt |> Option.defaultValue EmptyString
    //     let toCommonStr a b = Array.map2 toModelString a b |> Array.map pairOptionToStr |> joinStrings "#"
    //
    //     if defaultValues.Length = actualValues.Length then
    //         let pairedOptions = Array.map2 toModelString defaultValues actualValues
    //
    //         match pairedOptions |> Array.tryFindIndex (fun x -> x.IsSome) with
    //         | Some i ->
    //             match i with
    //             | 1 -> pairedOptions[1]
    //             | _ -> Array.map pairOptionToStr pairedOptions |> joinStrings separator |> Some
    //         | None -> None
    //     else if defaultValues.Length > actualValues.Length then
    //         let defaultValuesShort = defaultValues |> Array.take actualValues.Length
    //         let commonStr = toCommonStr defaultValuesShort actualValues
    //         let missingDefaults = defaultValues[actualValues.Length..] |> Array.map toDoubleString |> joinStrings "#"
    //         Some $"!{commonStr}{separator}{missingDefaults}"
    //     else
    //         let actualValuesShort = actualValues |> Array.take defaultValues.Length
    //         let commonStr = toCommonStr defaultValues actualValuesShort
    //         let extraActuals = actualValues[defaultValues.Length..] |> Array.map toDoubleString |> joinStrings "#"
    //         Some $"{commonStr}{separator}{extraActuals}"


    let poissonSample rnd lambda =
        if lambda <= 2e9 then
            // Use MathNet.Numerics.Distributions for small lambda
            try
                int64 (Poisson.Sample(rnd, lambda))
            with e ->
                failwith $"lambda: {lambda}, exception: {e}"
        else
            // Use Gaussian approximation for large lambda
            let mu = lambda
            let sigma = sqrt lambda
            let sample = Normal.Sample(rnd, mu, sigma)
            int64 (Math.Round(sample))


    /// Encapsulation of a Poisson distribution sampler.
    /// It takes a value of lambda and returns next random number of events.
    type PoissonSingleSampler =
        | PoissonSingleSampler of (float -> int64)

        member inline private r.value = let (PoissonSingleSampler v) = r in v
        member r.nextPoisson lambda = r.value lambda
        static member create rnd = poissonSample rnd |> PoissonSingleSampler


    /// Encapsulation of a Poisson distribution sampler factory suitable for both sequential and parallel code.
    type PoissonMultiSampler =
        {
            sampler : PoissonSingleSampler
            parallelSampler : PoissonSingleSampler[]
        }

        static member create n (rnd : Random) =
            let r() = Random(rnd.Next())
            let sampler = PoissonSingleSampler.create (r())
            let parallelSampler = [| for _ in 0..(n - 1) -> PoissonSingleSampler.create (r()) |]
            {
                sampler = sampler
                parallelSampler = parallelSampler
            }


    type PoissonSampler =
        | SingleSampler of PoissonSingleSampler
        | MultiSampler of PoissonMultiSampler

        member p.sampler =
            match p with
            | SingleSampler s -> s
            | MultiSampler s -> s.sampler

        member p.getSampler i =
            match p with
            | SingleSampler s -> s
            | MultiSampler s -> s.parallelSampler[i]

        member p.length =
            match p with
            | SingleSampler _ -> 0
            | MultiSampler s -> s.parallelSampler.Length

        static member createMultiSampler n rnd = PoissonMultiSampler.create n rnd |> MultiSampler
        static member createSingleSampler rnd = PoissonSingleSampler.create rnd |> SingleSampler


    type EvolutionType =
        | DifferentialEvolution
        | DiscreteEvolution


    /// Linear representation of a vector (array).
    type Vector<'T when ^T: (static member ( * ) : ^T * ^T -> ^T) and ^T: (static member ( + ) : ^T * ^T -> ^T) and ^T: (static member ( - ) : ^T * ^T -> ^T)> =
        | Vector of 'T[]

        member inline r.value = let (Vector v) = r in v

        static member inline (*) (a : 'T, b : Vector<'T>) : Vector<'T> =
            let retVal = b.value |> Array.map (fun e -> a * e) |> Vector
            retVal

        static member inline (*) (a : Vector<'T>, b : 'T) : Vector<'T> =
            let retVal = a.value |> Array.map (fun e -> e * b) |> Vector
            retVal

        static member inline (+) (a : Vector<'T>, b : Vector<'T>) : Vector<'T> =
            let retVal = a.value |> Array.mapi (fun i e -> e + b.value[i]) |> Vector
            retVal

        static member inline (-) (a : Vector<'T>, b : Vector<'T>) : Vector<'T> =
            let retVal = a.value |> Array.mapi (fun i e -> e - b.value[i]) |> Vector
            retVal


    /// Rectangular representation of a matrix.
    type Matrix<'T when ^T: (static member ( * ) : ^T * ^T -> ^T) and ^T: (static member ( + ) : ^T * ^T -> ^T) and ^T: (static member ( - ) : ^T * ^T -> ^T) and ^T: (static member Zero : ^T)> =
        | Matrix of 'T[][]

        member inline r.value = let (Matrix v) = r in v

        member inline r.total() = r.value |> Array.map (fun a -> a |> Array.sum) |> Array.sum
        member inline r.convert converter = r.value |> Array.map (fun a -> a |> Array.map converter) |> Matrix

        // /// Matrix multiplication (not implemented yet as it is not needed).
        // static member inline ( ** ) (a : Matrix<'T>, b : Matrix<'T>) : Matrix<'T> =
        //     failwith "Matrix multiplication is not implemented yet."

        static member inline (*) (a : 'T, b : Matrix<'T>) : Matrix<'T> =
            let retVal = b.value |> Array.map (fun e -> e |> Array.map (fun v -> a * v)) |> Matrix
            retVal

        static member inline (*) (a : Matrix<'T>, b : 'T) : Matrix<'T> =
            let retVal = a.value |> Array.map (fun e -> e |> Array.map (fun v -> v * b)) |> Matrix
            retVal

        /// This is NOT a matrix multiplication but element by element multiplication.
        /// The index of a Vector matches the FIRST index of a Matrix.
        static member inline (*) (a : Vector<'T>, b : Matrix<'T>) : Matrix<'T> =
            let retVal = b.value |> Array.mapi (fun i e -> e |> Array.map (fun v -> a.value[i] * v)) |> Matrix
            retVal

        /// This is NOT a matrix multiplication but element by element multiplication.
        /// The index of a Vector matches the SECOND index of a Matrix.
        static member inline (*) (a : Matrix<'T>, b : Vector<'T>) : Matrix<'T> =
            let retVal = a.value |> Array.map (fun e -> e |> Array.mapi (fun j v -> v * b.value[j])) |> Matrix
            retVal

        /// This is NOT a matrix multiplication but element by element multiplication.
        static member inline (*) (a : Matrix<'T>, b : Matrix<'T>) : Matrix<'T> =
            let retVal = a.value |> Array.mapi (fun i e -> e |> Array.mapi (fun j v -> v * b.value[i][j])) |> Matrix
            retVal

        static member inline (+) (a : Matrix<'T>, b : Matrix<'T>) : Matrix<'T> =
            let retVal = a.value |> Array.mapi (fun i e -> e |> Array.mapi (fun j v -> v + b.value[i][j])) |> Matrix
            retVal

        static member inline (-) (a : Matrix<'T>, b : Matrix<'T>) : Matrix<'T> =
            let retVal = a.value |> Array.mapi (fun i e -> e |> Array.mapi (fun j v -> v - b.value[i][j])) |> Matrix
            retVal


    /// Linear representation of a matrix to use with FORTRAN DLSODE ODE solver.
    type LinearMatrix<'T when ^T: (static member ( * ) : ^T * ^T -> ^T) and ^T: (static member ( + ) : ^T * ^T -> ^T) and ^T: (static member ( - ) : ^T * ^T -> ^T) and ^T: (static member Zero : ^T)> =
        {
            start : int // Beginning of the matrix data in the array.
            d1 : int // Size of the fist dimension.
            d2 : int  // Size of the second dimension.
            x : 'T[] // Array of at least (start + d1 * d2) length.
        }

        member inline m.getValue i j  = m.x[m.start + i * m.d1 + j]

        static member inline create (x : 'T[][]) =
            let d1 = x.Length
            let d2 = x[0].Length

            {
                start = 0
                d1 = d1
                d2 = d2
                x = [| for a in x do for b in a do yield b |]
            }

        member inline m.d1Range = [| for i in 0..(m.d1 - 1) -> i |]
        member inline m.d2Range = [| for i in 0..(m.d2 - 1) -> i |]

        member inline m.toMatrix() = m.d1Range |> Array.map (fun i -> m.d2Range |> Array.map (fun j -> m.getValue i j )) |> Matrix

        /// This is NOT a matrix multiplication but element by element multiplication.
        /// The index of a Vector matches the FIRST index of a Matrix.
        static member inline (*) (a : Vector<'T>, b : LinearMatrix<'T>) : Matrix<'T> =
            let retVal = b.d1Range |> Array.map (fun i -> b.d2Range |> Array.map (fun j -> a.value[i] * (b.getValue i j))) |> Matrix
            retVal

        /// This is NOT a matrix multiplication but element by element multiplication.
        /// The index of a Vector matches the SECOND index of a Matrix.
        static member inline (*) (a : LinearMatrix<'T>, b : Vector<'T>) : Matrix<'T> =
            let retVal = a.d1Range |> Array.map (fun i -> a.d2Range |> Array.map (fun j -> (a.getValue i j) * b.value[j])) |> Matrix
            retVal

        /// This is NOT a matrix multiplication but element by element multiplication.
        static member inline (*) (a : Matrix<'T>, b : LinearMatrix<'T>) : Matrix<'T> =
            let retVal = a.value |> Array.mapi (fun i e -> e |> Array.mapi (fun j v -> v * (b.getValue i j))) |> Matrix
            retVal

        /// This is NOT a matrix multiplication but element by element multiplication.
        static member inline (*) (a : LinearMatrix<'T>, b : Matrix<'T>) : Matrix<'T> =
            let retVal = b.value |> Array.mapi (fun i e -> e |> Array.mapi (fun j v -> (a.getValue i j) * v)) |> Matrix
            retVal


    type Matrix<'T when ^T: (static member ( * ) : ^T * ^T -> ^T) and ^T: (static member ( + ) : ^T * ^T -> ^T) and ^T: (static member ( - ) : ^T * ^T -> ^T) and ^T: (static member Zero : ^T)>
        with
        member inline m.toLinearMatrix() = LinearMatrix<'T>.create m.value


    type LinearDataType =
        | ScalarData
        | VectorData of int
        | MatrixData of int * int


    type LinearDataElement =
        {
            start : int
            dataType : LinearDataType
        }


    type LinearDataInfo<'K when 'K : comparison> =
        {
            dataTypes : Map<'K, LinearDataElement>
            start : int
        }

        static member defaultValue : LinearDataInfo<'K> =
            {
                dataTypes = Map.empty<'K, LinearDataElement>
                start = 0
            }


    /// A collection of various data (of the same type) packed into an array to be used with FORTRAN DLSODE ODE solver.
    /// The type is generic to simplify tests so that integers can be used for exact comparison.
    /// Otherwise, just double would do fine.
    type LinearData<'K, 'T when 'K : comparison and  ^T: (static member ( * ) : ^T * ^T -> ^T) and ^T: (static member ( + ) : ^T * ^T -> ^T) and ^T: (static member ( - ) : ^T * ^T -> ^T) and ^T: (static member Zero : ^T)> =
        {
            dataInfo : LinearDataInfo<'K>
            data : 'T[]
        }

        static member inline defaultValue : LinearData<'K, 'T> =
            {
                dataInfo = LinearDataInfo<'K>.defaultValue
                data = [||]
            }

        member inline d.Item
            with get i = d.dataInfo.dataTypes[i]

        member inline d1.append (k : 'K, d2 : 'T) : LinearData<'K, 'T> =
            if d1.dataInfo.dataTypes.ContainsKey k
            then failwith $"Cannot add the same key: '{k}' to the data collection."
            else
                {
                    d1 with
                        dataInfo =
                            {
                                d1.dataInfo with
                                    dataTypes = d1.dataInfo.dataTypes |> Map.add k { start = d1.dataInfo.start; dataType = ScalarData }
                                    start = d1.dataInfo.start + 1
                            }
                        data = Array.append d1.data [| d2 |]
                }

        member inline d1.append (k : 'K, d2 : Vector<'T>) : LinearData<'K, 'T> =
            if d1.dataInfo.dataTypes.ContainsKey k
            then failwith $"Cannot add the same key: '{k}' to the data collection."
            else
                {
                    d1 with
                        dataInfo =
                            {
                                d1.dataInfo with
                                    dataTypes = d1.dataInfo.dataTypes |> Map.add k { start = d1.dataInfo.start; dataType = VectorData d2.value.Length }
                                    start = d1.dataInfo.start + d2.value.Length
                            }
                        data = Array.append d1.data d2.value
                }

        member inline d1.append (k : 'K, d2 : Matrix<'T>) : LinearData<'K, 'T> =
            if d1.dataInfo.dataTypes.ContainsKey k
            then failwith $"Cannot add the same key: '{k}' to the data collection."
            else
                let n1 = d2.value.Length
                let n2 = d2.value[0].Length

                {
                    d1 with
                        dataInfo =
                            {
                                d1.dataInfo with
                                    dataTypes = d1.dataInfo.dataTypes |> Map.add k { start = d1.dataInfo.start; dataType = MatrixData (n1, n2) }
                                    start = d1.dataInfo.start + n1 * n2
                            }
                        data = Array.append d1.data (d2.value |> Array.concat)
                }

        static member inline create i d =
            {
                dataInfo = i
                data = d
            }


    /// A parameter to control when a value in a sparse array should be treated as exact zero (and ignored).
    type ZeroThreshold =
        | ZeroThreshold of double

        member r.value = let (ZeroThreshold v) = r in v
        static member defaultValue = ZeroThreshold 1.0e-05


    /// Representation of non-zero value in a sparse array.
    type SparseValue<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        {
            i : int
            value1D : 'T
        }


    [<RequireQualifiedAccess>]
    type SparseArray<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        {
            xValues : SparseValue<'T>[]
            xMap : Lazy<Map<int, 'T>>
        }

        member inline r.value = r.xValues
        member inline r.total() = r.value |> Array.map (fun e -> e.value1D) |> Array.sum

        static member inline private createLookupMap (values: SparseValue<'T>[]) =
            values
            |> Array.map (fun v -> v.i, v.value1D)
            |> Map.ofArray

        member inline a.tryFind i = a.xMap.Value.TryFind i

        static member inline create x =
            {
                xValues = x
                xMap = new Lazy<Map<int, double>>(fun () -> SparseArray.createLookupMap x)
            }

        static member inline createAbove (ZeroThreshold z) v =
            let x =
                v
                |> Array.mapi (fun i e -> if e >= z then Some { i = i; value1D = e } else None)
                |> Array.choose id

            {
                xValues = x
                xMap = new Lazy<Map<int, double>>(fun () -> SparseArray.createLookupMap x)
            }


    /// Representation of non-zero value in a sparse 2D array.
    type SparseValue2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        {
            i : int
            j : int
            value2D : 'T
        }

        /// Uses (i) as the first index.
        static member inline create i  (v : SparseValue<'T>) =
            {
                i = i
                j = v.i
                value2D = v.value1D
            }

        /// Uses (i) as the second index.
        static member inline createTransposed i (v : SparseValue<'T>) =
            {
                i = v.i
                j = i
                value2D = v.value1D
            }

        static member inline createArray i (x : SparseArray<'T>) = x.value |> Array.map (SparseValue2D.create i)


    type InseparableSparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        {
            xyValues : SparseValue2D<'T>[]
            xyMap : Lazy<Map<int * int, 'T>>
        }

        member inline a.tryFind i j = a.xyMap.Value.TryFind(i, j)

        static member inline private createLookupMap (values: SparseValue2D<'T>[]) =
            values
            |> Array.map (fun v -> (v.i, v.j), v.value2D)
            |> Map.ofArray

        static member inline create v =
            {
                xyValues = v
                xyMap = new Lazy<Map<int * int, 'T>>(fun () -> InseparableSparseArray2D.createLookupMap v)
            }


    type SeparableSparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        {
            xValues : SparseValue<'T>[]
            yValues : SparseValue<'T>[]
            xMap : Lazy<Map<int, 'T>>
            yMap : Lazy<Map<int, 'T>>
        }

        static member inline private createLookupMap (values: SparseValue<'T>[]) =
            values
            |> Array.map (fun v -> v.i, v.value1D)
            |> Map.ofArray

        member inline a.tryFind i j =
            match a.xMap.Value.TryFind i, a.yMap.Value.TryFind j with
            | Some x, Some y -> x * y |> Some
            | _ -> None

        static member inline create xValues yValues =
            {
                xValues = xValues
                yValues = yValues
                xMap = new Lazy<Map<int, 'T>>(fun () -> SeparableSparseArray2D.createLookupMap xValues)
                yMap = new Lazy<Map<int, 'T>>(fun () -> SeparableSparseArray2D.createLookupMap yValues)
            }

    /// SparseArray2D with an internal lookup map for performance
    /// See: https://github.com/dotnet/fsharp/issues/3302 for (*) operator.
    // [<RequireQualifiedAccess>]
    type SparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        | InseparableSparseArr2D of InseparableSparseArray2D<'T>
        | SeparableSparseArr2D of SeparableSparseArray2D<'T>

        // /// Access the raw array of sparse values
        // member inline r.value = let (SparseArray2D(v, _)) = r in v

        /// Generate the complete array of sparse values
        member inline r.getValues() =
            match r with
            | InseparableSparseArr2D a -> a.xyValues |> Seq.ofArray
            | SeparableSparseArr2D s ->
                seq {
                    for x in s.xValues do
                        for y in s.yValues do
                            yield {
                                i = x.i
                                j = y.i
                                value2D = x.value1D * y.value1D
                            }
                }

        /// Access the internal lookup map
        member inline r.tryFind i j =
            match r with
            | InseparableSparseArr2D a -> a.tryFind i j
            | SeparableSparseArr2D a -> a.tryFind i j

        /// Create a SparseArray2D from an array of SparseValue2D
        static member inline create (values: SparseValue2D<'T>[]) : SparseArray2D<'T> =
            InseparableSparseArray2D.create values |> InseparableSparseArr2D

        /// Create a separable SparseArray2D from two arrays of SparseValue
        static member inline create (xValues: SparseValue<'T>[], yValues: SparseValue<'T>[]) : SparseArray2D<'T> =
            SeparableSparseArray2D.create xValues yValues |> SeparableSparseArr2D

        // ================================================================




        /// Element-wise addition of two SparseArray2D instances
        static member inline (+) (a: SparseArray2D<'T>, b: SparseArray2D<'T>): SparseArray2D<'T> =
            match a, b with
            | SeparableSparseArr2D sa, SeparableSparseArr2D sb ->
                // For separable + separable
                // Addition of separable arrays always produces an inseparable result
                let allIndices =
                    seq {
                        for x in sa.xValues do
                            for y in sa.yValues do
                                yield (x.i, y.i)
                        for x in sb.xValues do
                            for y in sb.yValues do
                                yield (x.i, y.i)
                    } |> Set.ofSeq

                let resultValues =
                    allIndices
                    |> Set.toArray
                    |> Array.choose (fun (i, j) ->
                        let aVal = a.tryFind i j
                        let bVal = b.tryFind i j

                        match aVal, bVal with
                        | Some av, Some bv ->
                            let sum = av + bv
                            let zero = Unchecked.defaultof<'T>
                            if sum = zero then None
                            else Some { i = i; j = j; value2D = sum }
                        | Some av, None -> Some { i = i; j = j; value2D = av }
                        | None, Some bv -> Some { i = i; j = j; value2D = bv }
                        | None, None -> None
                    )

                SparseArray2D.create resultValues

            | SeparableSparseArr2D sa, InseparableSparseArr2D ib ->
                // For separable + inseparable
                let allIndices =
                    seq {
                        for x in sa.xValues do
                            for y in sa.yValues do
                                yield (x.i, y.i)
                        yield! ib.xyValues |> Array.map (fun v -> (v.i, v.j))
                    } |> Set.ofSeq

                let resultValues =
                    allIndices
                    |> Set.toArray
                    |> Array.choose (fun (i, j) ->
                        let aVal = a.tryFind i j
                        let bVal = b.tryFind i j

                        match aVal, bVal with
                        | Some av, Some bv ->
                            let sum = av + bv
                            let zero = Unchecked.defaultof<'T>
                            if sum = zero then None
                            else Some { i = i; j = j; value2D = sum }
                        | Some av, None -> Some { i = i; j = j; value2D = av }
                        | None, Some bv -> Some { i = i; j = j; value2D = bv }
                        | None, None -> None
                    )

                SparseArray2D.create resultValues

            | InseparableSparseArr2D ia, SeparableSparseArr2D sb ->
                // For inseparable + separable
                let allIndices =
                    seq {
                        yield! ia.xyValues |> Array.map (fun v -> (v.i, v.j))
                        for x in sb.xValues do
                            for y in sb.yValues do
                                yield (x.i, y.i)
                    } |> Set.ofSeq

                let resultValues =
                    allIndices
                    |> Set.toArray
                    |> Array.choose (fun (i, j) ->
                        let aVal = a.tryFind i j
                        let bVal = b.tryFind i j

                        match aVal, bVal with
                        | Some av, Some bv ->
                            let sum = av + bv
                            let zero = Unchecked.defaultof<'T>
                            if sum = zero then None
                            else Some { i = i; j = j; value2D = sum }
                        | Some av, None -> Some { i = i; j = j; value2D = av }
                        | None, Some bv -> Some { i = i; j = j; value2D = bv }
                        | None, None -> None
                    )

                SparseArray2D.create resultValues

            | InseparableSparseArr2D ia, InseparableSparseArr2D ib ->
                // For inseparable + inseparable
                let allIndices =
                    Set.union
                        (ia.xyValues |> Array.map (fun v -> (v.i, v.j)) |> Set.ofArray)
                        (ib.xyValues |> Array.map (fun v -> (v.i, v.j)) |> Set.ofArray)

                let resultValues =
                    allIndices
                    |> Set.toArray
                    |> Array.choose (fun (i, j) ->
                        let aVal = ia.tryFind i j
                        let bVal = ib.tryFind i j

                        match aVal, bVal with
                        | Some av, Some bv ->
                            let sum = av + bv
                            let zero = Unchecked.defaultof<'T>
                            if sum = zero then None
                            else Some { i = i; j = j; value2D = sum }
                        | Some av, None -> Some { i = i; j = j; value2D = av }
                        | None, Some bv -> Some { i = i; j = j; value2D = bv }
                        | None, None -> None
                    )

                SparseArray2D.create resultValues

        /// Element-wise subtraction of two SparseArray2D instances
        static member inline (-) (a: SparseArray2D<'T>, b: SparseArray2D<'T>): SparseArray2D<'T> =
            match a, b with
            | SeparableSparseArr2D sa, SeparableSparseArr2D sb ->
                // For separable - separable
                let allIndices =
                    seq {
                        for x in sa.xValues do
                            for y in sa.yValues do
                                yield (x.i, y.i)
                        for x in sb.xValues do
                            for y in sb.yValues do
                                yield (x.i, y.i)
                    } |> Set.ofSeq

                let resultValues =
                    allIndices
                    |> Set.toArray
                    |> Array.choose (fun (i, j) ->
                        let aVal = a.tryFind i j
                        let bVal = b.tryFind i j

                        match aVal, bVal with
                        | Some av, Some bv ->
                            let diff = av - bv
                            let zero = Unchecked.defaultof<'T>
                            if diff = zero then None
                            else Some { i = i; j = j; value2D = diff }
                        | Some av, None -> Some { i = i; j = j; value2D = av }
                        | None, Some bv ->
                            // Negate bv for subtraction
                            let negB = Unchecked.defaultof<'T> - bv
                            Some { i = i; j = j; value2D = negB }
                        | None, None -> None
                    )

                SparseArray2D.create resultValues

            | SeparableSparseArr2D sa, InseparableSparseArr2D ib ->
                // For separable - inseparable
                let allIndices =
                    seq {
                        for x in sa.xValues do
                            for y in sa.yValues do
                                yield (x.i, y.i)
                        yield! ib.xyValues |> Array.map (fun v -> (v.i, v.j))
                    } |> Set.ofSeq

                let resultValues =
                    allIndices
                    |> Set.toArray
                    |> Array.choose (fun (i, j) ->
                        let aVal = a.tryFind i j
                        let bVal = b.tryFind i j

                        match aVal, bVal with
                        | Some av, Some bv ->
                            let diff = av - bv
                            let zero = Unchecked.defaultof<'T>
                            if diff = zero then None
                            else Some { i = i; j = j; value2D = diff }
                        | Some av, None -> Some { i = i; j = j; value2D = av }
                        | None, Some bv ->
                            // Negate bv for subtraction
                            let negB = Unchecked.defaultof<'T> - bv
                            Some { i = i; j = j; value2D = negB }
                        | None, None -> None
                    )

                SparseArray2D.create resultValues

            | InseparableSparseArr2D ia, SeparableSparseArr2D sb ->
                // For inseparable - separable
                let allIndices =
                    seq {
                        yield! ia.xyValues |> Array.map (fun v -> (v.i, v.j))
                        for x in sb.xValues do
                            for y in sb.yValues do
                                yield (x.i, y.i)
                    } |> Set.ofSeq

                let resultValues =
                    allIndices
                    |> Set.toArray
                    |> Array.choose (fun (i, j) ->
                        let aVal = a.tryFind i j
                        let bVal = b.tryFind i j

                        match aVal, bVal with
                        | Some av, Some bv ->
                            let diff = av - bv
                            let zero = Unchecked.defaultof<'T>
                            if diff = zero then None
                            else Some { i = i; j = j; value2D = diff }
                        | Some av, None -> Some { i = i; j = j; value2D = av }
                        | None, Some bv ->
                            // Negate bv for subtraction
                            let negB = Unchecked.defaultof<'T> - bv
                            Some { i = i; j = j; value2D = negB }
                        | None, None -> None
                    )

                SparseArray2D.create resultValues

            | InseparableSparseArr2D ia, InseparableSparseArr2D ib ->
                // For inseparable - inseparable
                let allIndices =
                    Set.union
                        (ia.xyValues |> Array.map (fun v -> (v.i, v.j)) |> Set.ofArray)
                        (ib.xyValues |> Array.map (fun v -> (v.i, v.j)) |> Set.ofArray)

                let resultValues =
                    allIndices
                    |> Set.toArray
                    |> Array.choose (fun (i, j) ->
                        let aVal = ia.tryFind i j
                        let bVal = ib.tryFind i j

                        match aVal, bVal with
                        | Some av, Some bv ->
                            let diff = av - bv
                            let zero = Unchecked.defaultof<'T>
                            if diff = zero then None
                            else Some { i = i; j = j; value2D = diff }
                        | Some av, None -> Some { i = i; j = j; value2D = av }
                        | None, Some bv ->
                            // Negate bv for subtraction
                            let negB = Unchecked.defaultof<'T> - bv
                            Some { i = i; j = j; value2D = negB }
                        | None, None -> None
                    )

                SparseArray2D.create resultValues

        /// Element-wise multiplication of two SparseArray2D instances
        static member inline (*) (a: SparseArray2D<'T>, b: SparseArray2D<'T>): SparseArray2D<'T> =
            match a, b with
            | SeparableSparseArr2D sa, SeparableSparseArr2D sb ->
                // For separable * separable, the result is separable
                // We multiply the respective x and y components
                let resultXValues =
                    [|
                        for xA in sa.xValues do
                            for xB in sb.xValues do
                                if xA.i = xB.i then
                                    let product = xA.value1D * xB.value1D
                                    let zero = Unchecked.defaultof<'T>
                                    if product <> zero then
                                        yield { i = xA.i; value1D = product }
                    |]

                let resultYValues =
                    [|
                        for yA in sa.yValues do
                            for yB in sb.yValues do
                                if yA.i = yB.i then
                                    let product = yA.value1D * yB.value1D
                                    let zero = Unchecked.defaultof<'T>
                                    if product <> zero then
                                        yield { i = yA.i; value1D = product }
                    |]

                SparseArray2D.create (resultXValues, resultYValues)

            | SeparableSparseArr2D sa, InseparableSparseArr2D ib ->
                // For separable * inseparable
                let resultValues =
                    ib.xyValues
                    |> Array.choose (fun bVal ->
                        match sa.xMap.Value.TryFind bVal.i, sa.yMap.Value.TryFind bVal.j with
                        | Some xv, Some yv ->
                            let saValue = xv * yv
                            let product = saValue * bVal.value2D
                            let zero = Unchecked.defaultof<'T>
                            if product = zero then None
                            else Some { i = bVal.i; j = bVal.j; value2D = product }
                        | _ -> None
                    )

                SparseArray2D.create resultValues

            | InseparableSparseArr2D ia, SeparableSparseArr2D sb ->
                // For inseparable * separable
                let resultValues =
                    ia.xyValues
                    |> Array.choose (fun aVal ->
                        match sb.xMap.Value.TryFind aVal.i, sb.yMap.Value.TryFind aVal.j with
                        | Some xv, Some yv ->
                            let sbValue = xv * yv
                            let product = aVal.value2D * sbValue
                            let zero = Unchecked.defaultof<'T>
                            if product = zero then None
                            else Some { i = aVal.i; j = aVal.j; value2D = product }
                        | _ -> None
                    )

                SparseArray2D.create resultValues

            | InseparableSparseArr2D ia, InseparableSparseArr2D ib ->
                // For inseparable * inseparable
                let resultValues =
                    ia.xyValues
                    |> Array.choose (fun aVal ->
                        match ib.tryFind aVal.i aVal.j with
                        | Some bVal ->
                            let product = aVal.value2D * bVal
                            let zero = Unchecked.defaultof<'T>
                            if product = zero then None
                            else Some { i = aVal.i; j = aVal.j; value2D = product }
                        | None -> None
                    )

                SparseArray2D.create resultValues




        // ================================================================

        /// Multiplies a 2D sparse array by a scalar value.
        /// Returns a 2D sparse array.
        static member inline (*) (a : SparseArray2D<'U>, b : 'U) : SparseArray2D<'U> =
            match a with
            | InseparableSparseArr2D ia ->
                let v =
                    ia.xyValues
                    |> Array.map (fun e -> { e with value2D = e.value2D * b })
                    |> InseparableSparseArray2D.create

                InseparableSparseArr2D v
            | SeparableSparseArr2D sa ->
                let xValues =
                    sa.xValues
                    |> Array.map (fun e -> { e with value1D = e.value1D * b })

                SeparableSparseArray2D.create xValues sa.yValues |> SeparableSparseArr2D

        /// Multiplies a 2D sparse array by a scalar value.
        /// Returns a 2D sparse array.
        static member inline (*) (a : 'U, b : SparseArray2D<'U>) : SparseArray2D<'U> = b * a

        /// Multiplies a 2D sparse array by a 2D array (LinearMatrix) element by element.
        /// This is NOT a matrix multiplication.
        /// Returns a 2D sparse array.
        static member inline (*) (a : SparseArray2D<'T>, b : LinearMatrix<'T>) : SparseArray2D<'T> =
            // let v =
            //     a.value
            //     |> Array.map (fun e -> { e with value2D = e.value2D * (b.getValue e.i e.j) })
            //     |> SparseArray2D.create
            //
            // v
            failwith ""

        /// Multiplies a 2D sparse array by a 2D array (Matrix) element by element.
        /// This is NOT a matrix multiplication.
        /// Returns a 2D sparse array.
        static member inline (*) (a : SparseArray2D<'T>, b : Matrix<'T>) : SparseArray2D<'T> =
            // let v =
            //     a.value
            //     |> Array.map (fun e -> { e with value2D = e.value2D * b.value[e.i][e.j] })
            //     |> SparseArray2D.create
            //
            // v
            failwith ""

        static member inline createAbove z v =
            v
            |> Array.mapi (fun i e -> e |> Array.mapi (fun j v -> if v >= z then Some { i = i; j = j; value2D = v } else None))
            |> Array.concat
            |> Array.choose id
            |> SparseArray2D.create


    /// Representation of non-zero value in a sparse 4D array.
    type SparseValue4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        {
            i : int
            j : int
            i1 : int
            j1 : int
            value4D : 'T
        }

        /// Uses (i, j) as the first pair of indexes.
        static member inline create i j (v : SparseValue2D<'T>) =
            {
                i = i
                j = j
                i1 = v.i
                j1 = v.j
                value4D = v.value2D
            }

        /// Uses (i, j) as the second pair of indexes.
        static member inline createTransposed i j (v : SparseValue2D<'T>) =
            {
                i = v.i
                j = v.j
                i1 = i
                j1 = j
                value4D = v.value2D
            }

        static member inline createSeq i j (x : SparseArray2D<'T>) =
            x.getValues() |> Seq.map (SparseValue4D.create i j)


    type SparseValueArray4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        | SparseValueArray4D of SparseValue4D<'T>[]

        member inline r.value = let (SparseValueArray4D v) = r in v


    /// A static 4D representation of 4D sparse tensor where the first two indexes are full ([][] is used)
    /// and the last two are in a SparseArray2D.
    type StaticSparseArray4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        | StaticSparseArray4D of SparseArray2D<'T>[][]

        member inline r.value = let (StaticSparseArray4D v) = r in v

        /// Multiplies a 4D sparse array by a scalar value.
        /// Returns a 4D sparse array.
        static member inline (*) (a : StaticSparseArray4D<'U>, b : 'U) : StaticSparseArray4D<'U> =
            let v =
                a.value
                |> Array.map (fun e -> e |> Array.map (fun x -> x * b))
                |> StaticSparseArray4D

            v


    /// A dynamic 4D representation of 4D sparse tensor where the first two indexes are full
    /// and the last two are in a SparseArray2D.
    /// This is suitable for large sparse tensors where instantiation of [][] is not feasible.
    type DynamicSparseArrayData4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        {
            getData2D : int -> int -> SparseArray2D<'T>
            xLength : int
            yLength : int
        }


    type DynamicSparseArray4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        | DynamicSparseArray4D of DynamicSparseArrayData4D<'T>

        member inline private r.value = let (DynamicSparseArray4D v) = r in v
        member inline r.invoke = let (DynamicSparseArray4D v) = r in v.getData2D
        member inline r.xLength = let (DynamicSparseArray4D v) = r in v.xLength
        member inline r.yLength = let (DynamicSparseArray4D v) = r in v.yLength

        static member inline (*) (a : DynamicSparseArray4D<'T>, b : SparseArray2D<'T>) : DynamicSparseArray4D<'T> =
            let originalFunc = a.invoke

            // Create a new function that multiplies the result of the original function by b
            let newFunc i j : SparseArray2D<'T> =
                let sparseArray2D = originalFunc i j
                sparseArray2D * b

            { a.value with getData2D = newFunc } |> DynamicSparseArray4D


    type SparseArrayType =
        | StaticSparseArrayType
        | DynamicSparseArrayType


    /// A 4D representation of 4D sparse tensor where the first two indexes are full ([][] is used)
    /// and the last two are in a SparseArray2D.
    type SparseArray4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        | StaticSparseArr4D of StaticSparseArray4D<'T>
        | DynamicSparseArr4D of DynamicSparseArray4D<'T>

        // member inline r.value = let (SparseArray4D v) = r in v

        /// Multiplies a 4D sparse array by a 2D array (LinearMatrix) using SECOND pair of indexes in 4D array.
        /// This is NOT a matrix multiplication.
        /// Returns a 4D sparse array.
        static member inline (*) (a : SparseArray4D<'T>, b : LinearMatrix<'T>) : SparseArray4D<'T> =
            // let v =
            //     a.value
            //     |> Array.map (fun e -> e |> Array.map (fun x -> x * b))
            //     |> SparseArray4D
            //
            // v
            failwith ""

        /// Multiplies a 4D sparse array by a 2D array (Matrix) using SECOND pair of indexes in 4D array.
        /// This is NOT a matrix multiplication.
        /// Returns a 4D sparse array.
        static member inline (*) (a : SparseArray4D<'T>, b : Matrix<'T>) : SparseArray4D<'T> =
            match a with
            | StaticSparseArr4D s ->
                let v =
                    s.value
                    |> Array.map (fun e -> e |> Array.map (fun x -> x * b))
                    |> StaticSparseArray4D
                    |> StaticSparseArr4D

                v
            | DynamicSparseArr4D d ->
                failwith ""

        /// This is mostly for tests.
        member inline a.toSparseValueArray() : SparseValueArray4D<'T> =
            match a with
            | StaticSparseArr4D s ->
                let n1 = s.value.Length
                let n2 = s.value[0].Length

                let value =
                    [| for i in 0..(n1 - 1) -> [| for j in 0..(n2 - 1) -> SparseValue4D.createSeq i j (s.value[i][j]) |] |]
                    |> Array.concat
                    |> Seq.concat
                    |> Seq.toArray
                    |> Array.sortBy (fun e -> e.i, e.j, e.i1, e.j1)
                    |> SparseValueArray4D

                value

            | DynamicSparseArr4D d ->
                let n1 = d.xLength
                let n2 = d.yLength

                let value =
                    [| for i in 0..(n1 - 1) -> [| for j in 0..(n2 - 1) -> SparseValue4D.createSeq i j (d.invoke i j) |] |]
                    |> Array.concat
                    |> Seq.concat
                    |> Seq.toArray
                    |> Array.sortBy (fun e -> e.i, e.j, e.i1, e.j1)
                    |> SparseValueArray4D

                value


    /// Representation of non-zero value in a sparse 3D array.
    type SparseValue3D<'T> =
        {
            i : int
            j : int
            k : int
            value3D : 'T
        }


    [<RequireQualifiedAccess>]
    type SparseArray3D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        | SparseArray3D of SparseValue3D<'T>[]

        member inline r.value = let (SparseArray3D v) = r in v


    /// Representation of non-zero value in a sparse 6D array.
    type SparseValue6D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        {
            i : int
            j : int
            k : int
            i1 : int
            j1 : int
            k1 : int
            value6D : 'T
        }


    /// A 6D representation of 6D sparse tensor where the first three indexes are full ([][][] is used)
    /// and the last two are in a SparseArray3D.
    type SparseArray6D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> =
        | SparseArray6D of SparseArray3D<'T>[][][]

        member inline r.value = let (SparseArray6D v) = r in v


    /// Performs a Cartesian multiplication of two 1D sparse arrays to obtain a 2D sparse array.
    let inline cartesianMultiply<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                         and ^T: (static member ( + ) : ^T * ^T -> ^T)
                         and ^T: (static member ( - ) : ^T * ^T -> ^T)
                         and ^T: (static member Zero : ^T)
                         and ^T: equality> (a : SparseArray<'T>) (b : SparseArray<'T>) : SparseArray2D<'T> =
        let bValue = b.value
        a.value |> Array.map (fun e -> bValue |> Array.map (fun f -> { i = e.i; j = f.i; value2D = e.value1D * f.value1D })) |> Array.concat |> SparseArray2D.create
