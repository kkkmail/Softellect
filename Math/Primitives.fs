namespace Softellect.Math

open FSharp.Collections

module Primitives =

    type DistanceFunction<'C, 'V> =
        | DistanceFunction of ('C -> 'C -> 'V)

        member this.invoke = let (DistanceFunction v) = this in v


    /// Arithmetic operations record that encapsulates all required operations
    type ArithmeticOperations<'C> =
        {
            add: 'C -> 'C -> 'C
            subtract: 'C -> 'C -> 'C
            multiply: 'C -> 'C -> 'C
            divide: 'C -> 'C -> 'C
            dot: 'C -> 'C -> double
            multiplyByDouble: double -> 'C -> 'C
            sqrt: 'C -> 'C
            zero: 'C
            one: 'C
            toArray: ('C -> double[]) option
        }

        member ao.distance a b =
            let c = ao.subtract a b
            ao.dot c c |> sqrt


    /// Record containing conversion parameters
    type ConversionParameters<'I, 'C> =
        {
            arithmetic: ArithmeticOperations<'C>
            projector: 'I -> 'C
        }


    /// Based on https://github.com/TheAlgorithms/F-Sharp/blob/main/Algorithms/Strings/LevenshteinDistance.fs
    let rec levenshteinDistance (firstWord : string) (secondWord : string) : int =
        // The longer word should come first

        match secondWord.Length with
        | s when s > firstWord.Length -> levenshteinDistance secondWord firstWord
        | 0 -> firstWord.Length
        | _ ->
            let mutable previousRow = [ 0 .. secondWord.Length ]

            firstWord
            |> Seq.iteri
                (fun i c1 ->
                    let mutable currentRow = [ i + 1 ]

                    secondWord
                    |> Seq.iteri
                        (fun j c2 ->
                            let insertions = previousRow[j + 1] + 1
                            let deletions = currentRow[j] + 1

                            let substitutions =
                                previousRow[j] + (if c1 <> c2 then 1 else 0)

                            // Get the minimum to append to the current row
                            currentRow <-
                                currentRow
                                |> List.append [ (min insertions (min deletions substitutions)) ])

                    previousRow <- currentRow |> List.rev)

            previousRow |> List.rev |> List.item 0


    /// Needs a double to perform large summations and multiplications.
    let levenshteinDistanceFunction =
        DistanceFunction (fun a b -> (levenshteinDistance a b |> double))


    let cartesianDistanceFunction (ao : ArithmeticOperations<'C>) = DistanceFunction ao.distance


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
        // static member inline dot (a : Matrix<'T>, b : Matrix<'T>) : Matrix<'T> =
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
    type ZeroThreshold<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                     and ^T: (static member ( + ) : ^T * ^T -> ^T)
                     and ^T: (static member ( - ) : ^T * ^T -> ^T)
                     and ^T: (static member Zero : ^T)
                     and ^T: equality
                     and ^T: comparison> =
        | ZeroThreshold of 'T

        member inline r.value = let (ZeroThreshold v) = r in v


    type SparseArrayType =
        | StaticSparseArrayType
        | DynamicSparseArrayType


    // type DomainRange =
    //     {
    //         minValue : double
    //         maxValue : double
    //     }
    //
    //     member d.range = d.maxValue - d.minValue
    //
    //
    // /// Number of intervals in the domain.
    // type DomainIntervals =
    //     | DomainIntervals of int
    //
    //     member r.value = let (DomainIntervals v) = r in v
    //     static member defaultValue = DomainIntervals 100
    //
    //
    // /// Describes a function domain suitable for integral approximation.
    // /// Equidistant grid is used to reduce the number of multiplications.
    // type Domain =
    //     {
    //         points : Vector<double>
    //         step : double
    //         domainRange : DomainRange
    //     }
    //
    //     member d.noOfIntervals = d.points.value.Length - 1 |> DomainIntervals
    //     member d.normalize v = v * d.step
    //
    //     /// Number of points is (noOfIntervals + 1).
    //     static member create (n : DomainIntervals, r : DomainRange) =
    //         let noOfIntervals = n.value
    //         let range = r.range
    //         let rn = range / (double noOfIntervals)
    //         let points = [| for i in 0..noOfIntervals -> r.minValue + rn * (double i) |]
    //
    //         {
    //             points = Vector points
    //             step = range / (double noOfIntervals)
    //             domainRange = r
    //         }

    let factorial n = [ 1..n ] |> List.fold (*) 1


    /// Uses: x1 = xScale * (x - x0) in Taylor expansion.
    type TaylorApproximation =
        {
            x0 : double
            xScale : double
            coefficients : double[]
        }

        member ta.calculate x =
            let x1 = ta.xScale * (x - ta.x0)

            let retVal =
                ta.coefficients
                |> Array.mapi (fun i e -> (pown x1 i) * e / (factorial i |> double))
                |> Array.sum

            retVal

        member ta.comparisonFactors = [| ta.x0; ta.xScale |]


    // /// Uses: x1 = xScale * (x - x0) and y1 = yScale * (y - y0) in Taylor expansion.
    // ///
    // /// Each sub-array should contain the coefficients for all terms of a particular total order.
    // /// For example, if the highest order is 2, coefficients should be initialized as
    // /// [| [|a00|]; [|a10; a01|]; [|a20; a11; a02|] |],
    // /// where a20 is the coefficient of x^2, a11 of x * y, etc.
    // /// Note that the binomial coefficient is not included in the coefficients.
    // type TaylorApproximation2D =
    //     {
    //         x0 : double
    //         y0 : double
    //         xScale : double
    //         yScale : double
    //         coefficients : double[][]
    //     }
    //
    //     member ta.calculate (x, y) =
    //         let x1 = ta.xScale * (x - ta.x0)
    //         let y1 = ta.yScale * (y - ta.y0)
    //
    //         let retVal =
    //             ta.coefficients
    //             |> Array.mapi (fun i row ->
    //                 row
    //                 |> Array.mapi (fun j e ->
    //                     let binomial = factorial(i) / (factorial(j) * factorial(i - j)) |> double
    //                     (pown x1 j) * (pown y1 (i - j)) *  binomial * e / (factorial i |> double)))
    //             |> Array.concat
    //             |> Array.sum
    //
    //         retVal
    //
    //     member ta.comparisonFactors = [| ta.x0; ta.xScale; ta.y0; ta.yScale |]
    //
    //
    // /// Separate Taylor approximations for x and y spaces.
    // type SeparateTaylorApproximation2D =
    //     {
    //         xTaylorApproximation : TaylorApproximation
    //         yTaylorApproximation : TaylorApproximation
    //     }
    //
    //
    // type ScaledSeparateTaylorApproximation2D =
    //     {
    //         scale : double
    //         separateTaylorApproximation2D : SeparateTaylorApproximation2D
    //     }
    //
    //     member ta.comparisonFactors = Array.concat [| ta.separateTaylorApproximation2D.xTaylorApproximation.comparisonFactors;  ta.separateTaylorApproximation2D.yTaylorApproximation.comparisonFactors |]
    //
    //
    // type ScaledEeInfTaylorApproximation2D =
    //     {
    //         scale : double
    //         taylorApproximation2D : TaylorApproximation2D
    //     }
    //
    //     member ta.comparisonFactors = ta.taylorApproximation2D.comparisonFactors


    // ==============================================

    type DomainRange =
        {
            minValue : double
            maxValue : double
        }

        member d.range = d.maxValue - d.minValue
        static member defaultValue = { minValue = -1.0; maxValue = 1.0 }


    type DomainRange2D =
        {
            r0 : DomainRange
            r1 : DomainRange
        }

        static member defaultValue =
            {
                r0 = DomainRange.defaultValue
                r1 = DomainRange.defaultValue
            }


    type DomainRange3D =
        {
            r0 : DomainRange
            r1 : DomainRange
            r2 : DomainRange
        }

        static member defaultValue =
            {
                r0 = DomainRange.defaultValue
                r1 = DomainRange.defaultValue
                r2 = DomainRange.defaultValue
            }


    type DomainRange4D =
        {
            r0 : DomainRange
            r1 : DomainRange
            r2 : DomainRange
            r3 : DomainRange
        }

        static member defaultValue =
            {
                r0 = DomainRange.defaultValue
                r1 = DomainRange.defaultValue
                r2 = DomainRange.defaultValue
                r3 = DomainRange.defaultValue
            }


    type DomainRange5D =
        {
            r0 : DomainRange
            r1 : DomainRange
            r2 : DomainRange
            r3 : DomainRange
            r4 : DomainRange
        }

        static member defaultValue =
            {
                r0 = DomainRange.defaultValue
                r1 = DomainRange.defaultValue
                r2 = DomainRange.defaultValue
                r3 = DomainRange.defaultValue
                r4 = DomainRange.defaultValue
            }


    type DomainRange6D =
        {
            r0 : DomainRange
            r1 : DomainRange
            r2 : DomainRange
            r3 : DomainRange
            r4 : DomainRange
            r5 : DomainRange
        }

        static member defaultValue =
            {
                r0 = DomainRange.defaultValue
                r1 = DomainRange.defaultValue
                r2 = DomainRange.defaultValue
                r3 = DomainRange.defaultValue
                r4 = DomainRange.defaultValue
                r5 = DomainRange.defaultValue
            }


    type DomainRange7D =
        {
            r0 : DomainRange
            r1 : DomainRange
            r2 : DomainRange
            r3 : DomainRange
            r4 : DomainRange
            r5 : DomainRange
            r6 : DomainRange
        }

        static member defaultValue =
            {
                r0 = DomainRange.defaultValue
                r1 = DomainRange.defaultValue
                r2 = DomainRange.defaultValue
                r3 = DomainRange.defaultValue
                r4 = DomainRange.defaultValue
                r5 = DomainRange.defaultValue
                r6 = DomainRange.defaultValue
            }


    type DomainRange8D =
        {
            r0 : DomainRange
            r1 : DomainRange
            r2 : DomainRange
            r3 : DomainRange
            r4 : DomainRange
            r5 : DomainRange
            r6 : DomainRange
            r7 : DomainRange
        }

        static member defaultValue =
            {
                r0 = DomainRange.defaultValue
                r1 = DomainRange.defaultValue
                r2 = DomainRange.defaultValue
                r3 = DomainRange.defaultValue
                r4 = DomainRange.defaultValue
                r5 = DomainRange.defaultValue
                r6 = DomainRange.defaultValue
                r7 = DomainRange.defaultValue
            }


    /// Number of intervals in the domain.
    type DomainIntervals =
        | DomainIntervals of int

        member r.value = let (DomainIntervals v) = r in v
        static member defaultValue = DomainIntervals 100


    /// Describes a function domain suitable for integral approximation.
    /// Equidistant grid is used to reduce the number of multiplications.
    type Domain =
        {
            points : Vector<double>
            step : double
            domainRange : DomainRange
        }

        member d.noOfIntervals = d.points.value.Length - 1 |> DomainIntervals
        member d.normalize v = v * d.step
        member d.dimension = 1

        /// Number of points is (noOfIntervals + 1).
        static member create (n : DomainIntervals, r : DomainRange) =
            let noOfIntervals = n.value
            let range = r.range
            let rn = range / (double noOfIntervals)
            let points = [| for i in 0..noOfIntervals -> r.minValue + rn * (double i) |]

            {
                points = Vector points
                step = range / (double noOfIntervals)
                domainRange = r
            }

    /// 2D domain representation
    type Domain2D =
        {
            d0 : Domain
            d1 : Domain
        }

        static member create (n : DomainIntervals, r : DomainRange) =
            {
                d0 = Domain.create(n, r)
                d1 = Domain.create(n, r)
            }

        static member create (n : DomainIntervals, r : DomainRange2D) =
            {
                d0 = Domain.create(n, r.r0)
                d1 = Domain.create(n, r.r1)
            }

        member d.dimension = 2


    /// 3D domain representation
    type Domain3D =
        {
            d0 : Domain
            d1 : Domain
            d2 : Domain
        }

        static member create (n : DomainIntervals, r : DomainRange) =
            {
                d0 = Domain.create(n, r)
                d1 = Domain.create(n, r)
                d2 = Domain.create(n, r)
            }

        static member create (n : DomainIntervals, r : DomainRange3D) =
            {
                d0 = Domain.create(n, r.r0)
                d1 = Domain.create(n, r.r1)
                d2 = Domain.create(n, r.r2)
            }

        member d.dimension = 3


    /// 4D domain representation
    type Domain4D =
        {
            d0 : Domain
            d1 : Domain
            d2 : Domain
            d3 : Domain
        }

        static member create (n : DomainIntervals, r : DomainRange) =
            {
                d0 = Domain.create(n, r)
                d1 = Domain.create(n, r)
                d2 = Domain.create(n, r)
                d3 = Domain.create(n, r)
            }

        static member create (n : DomainIntervals, r : DomainRange4D) =
            {
                d0 = Domain.create(n, r.r0)
                d1 = Domain.create(n, r.r1)
                d2 = Domain.create(n, r.r2)
                d3 = Domain.create(n, r.r3)
            }

        member d.dimension = 4


    /// 5D domain representation
    type Domain5D =
        {
            d0 : Domain
            d1 : Domain
            d2 : Domain
            d3 : Domain
            d4 : Domain
        }

        static member create (n : DomainIntervals, r : DomainRange) =
            {
                d0 = Domain.create(n, r)
                d1 = Domain.create(n, r)
                d2 = Domain.create(n, r)
                d3 = Domain.create(n, r)
                d4 = Domain.create(n, r)
            }

        static member create (n : DomainIntervals, r : DomainRange5D) =
            {
                d0 = Domain.create(n, r.r0)
                d1 = Domain.create(n, r.r1)
                d2 = Domain.create(n, r.r2)
                d3 = Domain.create(n, r.r3)
                d4 = Domain.create(n, r.r4)
            }

        member d.dimension = 5


    /// 6D domain representation
    type Domain6D =
        {
            d0 : Domain
            d1 : Domain
            d2 : Domain
            d3 : Domain
            d4 : Domain
            d5 : Domain
        }

        static member create (n : DomainIntervals, r : DomainRange) =
            {
                d0 = Domain.create(n, r)
                d1 = Domain.create(n, r)
                d2 = Domain.create(n, r)
                d3 = Domain.create(n, r)
                d4 = Domain.create(n, r)
                d5 = Domain.create(n, r)
            }

        static member create (n : DomainIntervals, r : DomainRange6D) =
            {
                d0 = Domain.create(n, r.r0)
                d1 = Domain.create(n, r.r1)
                d2 = Domain.create(n, r.r2)
                d3 = Domain.create(n, r.r3)
                d4 = Domain.create(n, r.r4)
                d5 = Domain.create(n, r.r5)
            }

        member d.dimension = 6


    /// 7D domain representation
    type Domain7D =
        {
            d0 : Domain
            d1 : Domain
            d2 : Domain
            d3 : Domain
            d4 : Domain
            d5 : Domain
            d6 : Domain
        }

        static member create (n : DomainIntervals, r : DomainRange) =
            {
                d0 = Domain.create(n, r)
                d1 = Domain.create(n, r)
                d2 = Domain.create(n, r)
                d3 = Domain.create(n, r)
                d4 = Domain.create(n, r)
                d5 = Domain.create(n, r)
                d6 = Domain.create(n, r)
            }

        static member create (n : DomainIntervals, r : DomainRange7D) =
            {
                d0 = Domain.create(n, r.r0)
                d1 = Domain.create(n, r.r1)
                d2 = Domain.create(n, r.r2)
                d3 = Domain.create(n, r.r3)
                d4 = Domain.create(n, r.r4)
                d5 = Domain.create(n, r.r5)
                d6 = Domain.create(n, r.r6)
            }

        member d.dimension = 7


    /// 8D domain representation
    type Domain8D =
        {
            d0 : Domain
            d1 : Domain
            d2 : Domain
            d3 : Domain
            d4 : Domain
            d5 : Domain
            d6 : Domain
            d7 : Domain
        }

        static member create (n : DomainIntervals, r : DomainRange) =
            {
                d0 = Domain.create(n, r)
                d1 = Domain.create(n, r)
                d2 = Domain.create(n, r)
                d3 = Domain.create(n, r)
                d4 = Domain.create(n, r)
                d5 = Domain.create(n, r)
                d6 = Domain.create(n, r)
                d7 = Domain.create(n, r)
            }

        static member create (n : DomainIntervals, r : DomainRange8D) =
            {
                d0 = Domain.create(n, r.r0)
                d1 = Domain.create(n, r.r1)
                d2 = Domain.create(n, r.r2)
                d3 = Domain.create(n, r.r3)
                d4 = Domain.create(n, r.r4)
                d5 = Domain.create(n, r.r5)
                d6 = Domain.create(n, r.r6)
                d7 = Domain.create(n, r.r7)
            }

        member d.dimension = 8


    /// 1D coordinate representation.
    type Coord1D =
        {
            x0 : double
        }

        static member Zero = { x0 = 0.0 }
        static member One = { x0 = 1.0 }
        static member (+) (a : Coord1D, b : Coord1D) = { x0 = a.x0 + b.x0 }
        static member (-) (a : Coord1D, b : Coord1D) = { x0 = a.x0 - b.x0 }
        static member (*) (a : Coord1D, b : Coord1D) = { x0 = a.x0 * b.x0 }
        static member ( .* ) (d : double, a : Coord1D) = { x0 = d * a.x0 }
        static member ( *. ) (a : Coord1D, d : double) = d .* a
        static member (/) (a : Coord1D, b : Coord1D) = { x0 = a.x0 / b.x0 }
        static member ( /. ) (a : Coord1D, d : double) = a *. (1.0 / d)
        static member dot (a : Coord1D) (b : Coord1D) = a.x0 * b.x0
        static member ( ** ) (a : Coord1D, b : Coord1D) = Coord1D.dot a b
        member this.total() = this.x0
        member this.sqrt() = { x0 = sqrt (abs this.x0) }


    /// 2D coordinate representation.
    type Coord2D =
        {
            x0 : double
            x1 : double
        }

        static member Zero = { x0 = 0.0; x1 = 0.0 }
        static member One = { x0 = 1.0; x1 = 1.0 }
        static member (+) (a : Coord2D, b : Coord2D) = { x0 = a.x0 + b.x0; x1 = a.x1 + b.x1 }
        static member (-) (a : Coord2D, b : Coord2D) = { x0 = a.x0 - b.x0; x1 = a.x1 - b.x1 }
        static member (*) (a : Coord2D, b : Coord2D) = { x0 = a.x0 * b.x0; x1 = a.x1 * b.x1 }
        static member ( .* ) (d : double, a : Coord2D) = { x0 = d * a.x0; x1 = d * a.x1 }
        static member ( *. ) (a : Coord2D, d : double) = d .* a
        static member (/) (a : Coord2D, b : Coord2D) = { x0 = a.x0 / b.x0; x1 = a.x1 / b.x1 }
        static member ( /. ) (a : Coord2D, d : double) = a *. (1.0 / d)
        static member dot (a : Coord2D) (b : Coord2D) = a.x0 * b.x0 + a.x1 * b.x1
        static member ( ** ) (a : Coord2D, b : Coord2D) = Coord2D.dot a b
        member this.total() = this.x0 + this.x1
        member this.sqrt() = { x0 = sqrt (abs this.x0); x1 = sqrt (abs this.x1) }


    /// 3D coordinate representation.
    type Coord3D =
        {
            x0 : double
            x1 : double
            x2 : double
        }

        static member Zero = { x0 = 0.0; x1 = 0.0; x2 = 0.0 }
        static member One = { x0 = 1.0; x1 = 1.0; x2 = 1.0 }
        static member (+) (a : Coord3D, b : Coord3D) = { x0 = a.x0 + b.x0; x1 = a.x1 + b.x1; x2 = a.x2 + b.x2 }
        static member (-) (a : Coord3D, b : Coord3D) = { x0 = a.x0 - b.x0; x1 = a.x1 - b.x1; x2 = a.x2 - b.x2 }
        static member (*) (a : Coord3D, b : Coord3D) = { x0 = a.x0 * b.x0; x1 = a.x1 * b.x1; x2 = a.x2 * b.x2 }
        static member ( .* ) (d : double, a : Coord3D) = { x0 = d * a.x0; x1 = d * a.x1; x2 = d * a.x2 }
        static member ( *. ) (a : Coord3D, d : double) = d .* a
        static member (/) (a : Coord3D, b : Coord3D) = { x0 = a.x0 / b.x0; x1 = a.x1 / b.x1; x2 = a.x2 / b.x2 }
        static member ( /. ) (a : Coord3D, d : double) = a *. (1.0 / d)
        static member dot (a : Coord3D) (b : Coord3D) = a.x0 * b.x0 + a.x1 * b.x1 + a.x2 * b.x2
        static member ( ** ) (a : Coord3D, b : Coord3D) = Coord3D.dot a b
        member this.total() = this.x0 + this.x1 + this.x2
        member this.sqrt() = { x0 = sqrt (abs this.x0); x1 = sqrt (abs this.x1); x2 = sqrt (abs this.x2) }


    /// 4D coordinate representation.
    type Coord4D =
        {
            x0 : double
            x1 : double
            x2 : double
            x3 : double
        }

        static member Zero = { x0 = 0.0; x1 = 0.0; x2 = 0.0; x3 = 0.0 }
        static member One = { x0 = 1.0; x1 = 1.0; x2 = 1.0; x3 = 1.0 }
        static member (+) (a : Coord4D, b : Coord4D) = { x0 = a.x0 + b.x0; x1 = a.x1 + b.x1; x2 = a.x2 + b.x2; x3 = a.x3 + b.x3 }
        static member (-) (a : Coord4D, b : Coord4D) = { x0 = a.x0 - b.x0; x1 = a.x1 - b.x1; x2 = a.x2 - b.x2; x3 = a.x3 - b.x3 }
        static member (*) (a : Coord4D, b : Coord4D) = { x0 = a.x0 * b.x0; x1 = a.x1 * b.x1; x2 = a.x2 * b.x2; x3 = a.x3 * b.x3 }
        static member ( .* ) (d : double, a : Coord4D) = { x0 = d * a.x0; x1 = d * a.x1; x2 = d * a.x2; x3 = d * a.x3 }
        static member ( *. ) (a : Coord4D, d : double) = d .* a
        static member (/) (a : Coord4D, b : Coord4D) = { x0 = a.x0 / b.x0; x1 = a.x1 / b.x1; x2 = a.x2 / b.x2; x3 = a.x3 / b.x3 }
        static member ( /. ) (a : Coord4D, d : double) = a *. (1.0 / d)
        static member dot (a : Coord4D) (b : Coord4D) = a.x0 * b.x0 + a.x1 * b.x1 + a.x2 * b.x2 + a.x3 * b.x3
        static member ( ** ) (a : Coord4D, b : Coord4D) = Coord4D.dot a b
        member this.total() = this.x0 + this.x1 + this.x2 + this.x3
        member this.sqrt() = { x0 = sqrt (abs this.x0); x1 = sqrt (abs this.x1); x2 = sqrt (abs this.x2); x3 = sqrt (abs this.x3) }


    /// 5D coordinate representation.
    type Coord5D =
        {
            x0 : double
            x1 : double
            x2 : double
            x3 : double
            x4 : double
        }

        static member Zero = { x0 = 0.0; x1 = 0.0; x2 = 0.0; x3 = 0.0; x4 = 0.0 }
        static member One = { x0 = 1.0; x1 = 1.0; x2 = 1.0; x3 = 1.0; x4 = 1.0 }
        static member (+) (a : Coord5D, b : Coord5D) = { x0 = a.x0 + b.x0; x1 = a.x1 + b.x1; x2 = a.x2 + b.x2; x3 = a.x3 + b.x3; x4 = a.x4 + b.x4 }
        static member (-) (a : Coord5D, b : Coord5D) = { x0 = a.x0 - b.x0; x1 = a.x1 - b.x1; x2 = a.x2 - b.x2; x3 = a.x3 - b.x3; x4 = a.x4 - b.x4 }
        static member (*) (a : Coord5D, b : Coord5D) = { x0 = a.x0 * b.x0; x1 = a.x1 * b.x1; x2 = a.x2 * b.x2; x3 = a.x3 * b.x3; x4 = a.x4 * b.x4 }
        static member ( .* ) (d : double, a : Coord5D) = { x0 = d * a.x0; x1 = d * a.x1; x2 = d * a.x2; x3 = d * a.x3; x4 = d * a.x4 }
        static member ( *. ) (a : Coord5D, d : double) = d .* a
        static member (/) (a : Coord5D, b : Coord5D) = { x0 = a.x0 / b.x0; x1 = a.x1 / b.x1; x2 = a.x2 / b.x2; x3 = a.x3 / b.x3; x4 = a.x4 / b.x4 }
        static member ( /. ) (a : Coord5D, d : double) = a *. (1.0 / d)
        static member dot (a : Coord5D) (b : Coord5D) = a.x0 * b.x0 + a.x1 * b.x1 + a.x2 * b.x2 + a.x3 * b.x3 + a.x4 * b.x4
        static member ( ** ) (a : Coord5D, b : Coord5D) = Coord5D.dot a b
        member this.total() = this.x0 + this.x1 + this.x2 + this.x3 + this.x4
        member this.sqrt() = { x0 = sqrt (abs this.x0); x1 = sqrt (abs this.x1); x2 = sqrt (abs this.x2); x3 = sqrt (abs this.x3); x4 = sqrt (abs this.x4) }


    /// 6D coordinate representation.
    type Coord6D =
        {
            x0 : double
            x1 : double
            x2 : double
            x3 : double
            x4 : double
            x5 : double
        }

        static member Zero = { x0 = 0.0; x1 = 0.0; x2 = 0.0; x3 = 0.0; x4 = 0.0; x5 = 0.0 }
        static member One = { x0 = 1.0; x1 = 1.0; x2 = 1.0; x3 = 1.0; x4 = 1.0; x5 = 1.0 }
        static member (+) (a : Coord6D, b : Coord6D) = { x0 = a.x0 + b.x0; x1 = a.x1 + b.x1; x2 = a.x2 + b.x2; x3 = a.x3 + b.x3; x4 = a.x4 + b.x4; x5 = a.x5 + b.x5 }
        static member (-) (a : Coord6D, b : Coord6D) = { x0 = a.x0 - b.x0; x1 = a.x1 - b.x1; x2 = a.x2 - b.x2; x3 = a.x3 - b.x3; x4 = a.x4 - b.x4; x5 = a.x5 - b.x5 }
        static member (*) (a : Coord6D, b : Coord6D) = { x0 = a.x0 * b.x0; x1 = a.x1 * b.x1; x2 = a.x2 * b.x2; x3 = a.x3 * b.x3; x4 = a.x4 * b.x4; x5 = a.x5 * b.x5 }
        static member ( .* ) (d : double, a : Coord6D) = { x0 = d * a.x0; x1 = d * a.x1; x2 = d * a.x2; x3 = d * a.x3; x4 = d * a.x4; x5 = d * a.x5 }
        static member ( *. ) (a : Coord6D, d : double) = d .* a
        static member (/) (a : Coord6D, b : Coord6D) = { x0 = a.x0 / b.x0; x1 = a.x1 / b.x1; x2 = a.x2 / b.x2; x3 = a.x3 / b.x3; x4 = a.x4 / b.x4; x5 = a.x5 / b.x5 }
        static member ( /. ) (a : Coord6D, d : double) = a *. (1.0 / d)
        static member dot (a : Coord6D) (b : Coord6D) = a.x0 * b.x0 + a.x1 * b.x1 + a.x2 * b.x2 + a.x3 * b.x3 + a.x4 * b.x4 + a.x5 * b.x5
        static member ( ** ) (a : Coord6D, b : Coord6D) = Coord6D.dot a b
        member this.total() = this.x0 + this.x1 + this.x2 + this.x3 + this.x4 + this.x5
        member this.sqrt() = { x0 = sqrt (abs this.x0); x1 = sqrt (abs this.x1); x2 = sqrt (abs this.x2); x3 = sqrt (abs this.x3); x4 = sqrt (abs this.x4); x5 = sqrt (abs this.x5) }


    /// 7D coordinate representation.
    type Coord7D =
        {
            x0 : double
            x1 : double
            x2 : double
            x3 : double
            x4 : double
            x5 : double
            x6 : double
        }

        static member Zero = { x0 = 0.0; x1 = 0.0; x2 = 0.0; x3 = 0.0; x4 = 0.0; x5 = 0.0; x6 = 0.0 }
        static member One = { x0 = 1.0; x1 = 1.0; x2 = 1.0; x3 = 1.0; x4 = 1.0; x5 = 1.0; x6 = 1.0 }
        static member (+) (a : Coord7D, b : Coord7D) = { x0 = a.x0 + b.x0; x1 = a.x1 + b.x1; x2 = a.x2 + b.x2; x3 = a.x3 + b.x3; x4 = a.x4 + b.x4; x5 = a.x5 + b.x5; x6 = a.x6 + b.x6 }
        static member (-) (a : Coord7D, b : Coord7D) = { x0 = a.x0 - b.x0; x1 = a.x1 - b.x1; x2 = a.x2 - b.x2; x3 = a.x3 - b.x3; x4 = a.x4 - b.x4; x5 = a.x5 - b.x5; x6 = a.x6 - b.x6 }
        static member (*) (a : Coord7D, b : Coord7D) = { x0 = a.x0 * b.x0; x1 = a.x1 * b.x1; x2 = a.x2 * b.x2; x3 = a.x3 * b.x3; x4 = a.x4 * b.x4; x5 = a.x5 * b.x5; x6 = a.x6 * b.x6 }
        static member ( .* ) (d : double, a : Coord7D) = { x0 = d * a.x0; x1 = d * a.x1; x2 = d * a.x2; x3 = d * a.x3; x4 = d * a.x4; x5 = d * a.x5; x6 = d * a.x6 }
        static member ( *. ) (a : Coord7D, d : double) = d .* a
        static member (/) (a : Coord7D, b : Coord7D) = { x0 = a.x0 / b.x0; x1 = a.x1 / b.x1; x2 = a.x2 / b.x2; x3 = a.x3 / b.x3; x4 = a.x4 / b.x4; x5 = a.x5 / b.x5; x6 = a.x6 / b.x6 }
        static member ( /. ) (a : Coord7D, d : double) = a *. (1.0 / d)
        static member dot (a : Coord7D) (b : Coord7D) = a.x0 * b.x0 + a.x1 * b.x1 + a.x2 * b.x2 + a.x3 * b.x3 + a.x4 * b.x4 + a.x5 * b.x5 + a.x6 * b.x6
        static member ( ** ) (a : Coord7D, b : Coord7D) = Coord7D.dot a b
        member this.total() = this.x0 + this.x1 + this.x2 + this.x3 + this.x4 + this.x5 + this.x6
        member this.sqrt() = { x0 = sqrt (abs this.x0); x1 = sqrt (abs this.x1); x2 = sqrt (abs this.x2); x3 = sqrt (abs this.x3); x4 = sqrt (abs this.x4); x5 = sqrt (abs this.x5); x6 = sqrt (abs this.x6) }


    /// 8D coordinate representation.
    type Coord8D =
        {
            x0 : double
            x1 : double
            x2 : double
            x3 : double
            x4 : double
            x5 : double
            x6 : double
            x7 : double
        }

        static member Zero = { x0 = 0.0; x1 = 0.0; x2 = 0.0; x3 = 0.0; x4 = 0.0; x5 = 0.0; x6 = 0.0; x7 = 0.0 }
        static member One = { x0 = 1.0; x1 = 1.0; x2 = 1.0; x3 = 1.0; x4 = 1.0; x5 = 1.0; x6 = 1.0; x7 = 1.0 }
        static member (+) (a : Coord8D, b : Coord8D) = { x0 = a.x0 + b.x0; x1 = a.x1 + b.x1; x2 = a.x2 + b.x2; x3 = a.x3 + b.x3; x4 = a.x4 + b.x4; x5 = a.x5 + b.x5; x6 = a.x6 + b.x6; x7 = a.x7 + b.x7 }
        static member (-) (a : Coord8D, b : Coord8D) = { x0 = a.x0 - b.x0; x1 = a.x1 - b.x1; x2 = a.x2 - b.x2; x3 = a.x3 - b.x3; x4 = a.x4 - b.x4; x5 = a.x5 - b.x5; x6 = a.x6 - b.x6; x7 = a.x7 - b.x7 }
        static member (*) (a : Coord8D, b : Coord8D) = { x0 = a.x0 * b.x0; x1 = a.x1 * b.x1; x2 = a.x2 * b.x2; x3 = a.x3 * b.x3; x4 = a.x4 * b.x4; x5 = a.x5 * b.x5; x6 = a.x6 * b.x6; x7 = a.x7 * b.x7 }
        static member ( .* ) (d : double, a : Coord8D) = { x0 = d * a.x0; x1 = d * a.x1; x2 = d * a.x2; x3 = d * a.x3; x4 = d * a.x4; x5 = d * a.x5; x6 = d * a.x6; x7 = d * a.x7 }
        static member ( *. ) (a : Coord8D, d : double) = d .* a
        static member (/) (a : Coord8D, b : Coord8D) = { x0 = a.x0 / b.x0; x1 = a.x1 / b.x1; x2 = a.x2 / b.x2; x3 = a.x3 / b.x3; x4 = a.x4 / b.x4; x5 = a.x5 / b.x5; x6 = a.x6 / b.x6; x7 = a.x7 / b.x7 }
        static member ( /. ) (a : Coord8D, d : double) = a *. (1.0 / d)
        static member dot (a : Coord8D) (b : Coord8D) = a.x0 * b.x0 + a.x1 * b.x1 + a.x2 * b.x2 + a.x3 * b.x3 + a.x4 * b.x4 + a.x5 * b.x5 + a.x6 * b.x6 + a.x7 * b.x7
        static member ( ** ) (a : Coord8D, b : Coord8D) = Coord8D.dot a b
        member this.total() = this.x0 + this.x1 + this.x2 + this.x3 + this.x4 + this.x5 + this.x6 + this.x7
        member this.sqrt() = { x0 = sqrt (abs this.x0); x1 = sqrt (abs this.x1); x2 = sqrt (abs this.x2); x3 = sqrt (abs this.x3); x4 = sqrt (abs this.x4); x5 = sqrt (abs this.x5); x6 = sqrt (abs this.x6); x7 = sqrt (abs this.x7) }


    /// 1D point representation
    type Point1D =
        {
            i0 : int
        }

        member p.toCoord (d : Domain) =
            {
                x0 = d.points.value[p.i0]
            }

        member p.boundaryCount (d : int) =
            (if p.i0 = 0 || p.i0 = d - 1 then 1 else 0)


    /// 2D point representation
    type Point2D =
        {
            i0 : int
            i1 : int
        }

        member p.toCoord (d : Domain2D) =
            {
                x0 = d.d0.points.value[p.i0]
                x1 = d.d1.points.value[p.i1]
            }

        member p.boundaryCount (d : int) =
            (if p.i0 = 0 || p.i0 = d - 1 then 1 else 0) +
            (if p.i1 = 0 || p.i1 = d - 1 then 1 else 0)


    /// 3D point representation
    type Point3D =
        {
            i0 : int
            i1 : int
            i2 : int
        }

        member p.toCoord (d : Domain3D) =
            {
                x0 = d.d0.points.value[p.i0]
                x1 = d.d1.points.value[p.i1]
                x2 = d.d2.points.value[p.i2]
            }

        member p.boundaryCount (d : int) =
            (if p.i0 = 0 || p.i0 = d - 1 then 1 else 0) +
            (if p.i1 = 0 || p.i1 = d - 1 then 1 else 0) +
            (if p.i2 = 0 || p.i2 = d - 1 then 1 else 0)


    /// 4D point representation
    type Point4D =
        {
            i0 : int
            i1 : int
            i2 : int
            i3 : int
        }

        member p.toCoord (d : Domain4D) =
            {
                x0 = d.d0.points.value[p.i0]
                x1 = d.d1.points.value[p.i1]
                x2 = d.d2.points.value[p.i2]
                x3 = d.d3.points.value[p.i3]
            }

        member p.boundaryCount (d : int) =
            (if p.i0 = 0 || p.i0 = d - 1 then 1 else 0) +
            (if p.i1 = 0 || p.i1 = d - 1 then 1 else 0) +
            (if p.i2 = 0 || p.i2 = d - 1 then 1 else 0) +
            (if p.i3 = 0 || p.i3 = d - 1 then 1 else 0)


    /// 5D point representation
    type Point5D =
        {
            i0 : int
            i1 : int
            i2 : int
            i3 : int
            i4 : int
        }

        member p.toCoord (d : Domain5D) =
            {
                x0 = d.d0.points.value[p.i0]
                x1 = d.d1.points.value[p.i1]
                x2 = d.d2.points.value[p.i2]
                x3 = d.d3.points.value[p.i3]
                x4 = d.d4.points.value[p.i4]
            }

        member p.boundaryCount (d : int) =
            (if p.i0 = 0 || p.i0 = d - 1 then 1 else 0) +
            (if p.i1 = 0 || p.i1 = d - 1 then 1 else 0) +
            (if p.i2 = 0 || p.i2 = d - 1 then 1 else 0) +
            (if p.i3 = 0 || p.i3 = d - 1 then 1 else 0) +
            (if p.i4 = 0 || p.i4 = d - 1 then 1 else 0)


    /// 6D point representation
    type Point6D =
        {
            i0 : int
            i1 : int
            i2 : int
            i3 : int
            i4 : int
            i5 : int
        }

        member p.toCoord (d : Domain6D) =
            {
                x0 = d.d0.points.value[p.i0]
                x1 = d.d1.points.value[p.i1]
                x2 = d.d2.points.value[p.i2]
                x3 = d.d3.points.value[p.i3]
                x4 = d.d4.points.value[p.i4]
                x5 = d.d5.points.value[p.i5]
            }

        member p.boundaryCount (d : int) =
            (if p.i0 = 0 || p.i0 = d - 1 then 1 else 0) +
            (if p.i1 = 0 || p.i1 = d - 1 then 1 else 0) +
            (if p.i2 = 0 || p.i2 = d - 1 then 1 else 0) +
            (if p.i3 = 0 || p.i3 = d - 1 then 1 else 0) +
            (if p.i4 = 0 || p.i4 = d - 1 then 1 else 0) +
            (if p.i5 = 0 || p.i5 = d - 1 then 1 else 0)


    /// 7D point representation
    type Point7D =
        {
            i0 : int
            i1 : int
            i2 : int
            i3 : int
            i4 : int
            i5 : int
            i6 : int
        }

        member p.toCoord (d : Domain7D) =
            {
                x0 = d.d0.points.value[p.i0]
                x1 = d.d1.points.value[p.i1]
                x2 = d.d2.points.value[p.i2]
                x3 = d.d3.points.value[p.i3]
                x4 = d.d4.points.value[p.i4]
                x5 = d.d5.points.value[p.i5]
                x6 = d.d6.points.value[p.i6]
            }

        member p.boundaryCount (d : int) =
            (if p.i0 = 0 || p.i0 = d - 1 then 1 else 0) +
            (if p.i1 = 0 || p.i1 = d - 1 then 1 else 0) +
            (if p.i2 = 0 || p.i2 = d - 1 then 1 else 0) +
            (if p.i3 = 0 || p.i3 = d - 1 then 1 else 0) +
            (if p.i4 = 0 || p.i4 = d - 1 then 1 else 0) +
            (if p.i5 = 0 || p.i5 = d - 1 then 1 else 0) +
            (if p.i6 = 0 || p.i6 = d - 1 then 1 else 0)


    /// 8D point representation
    type Point8D =
        {
            i0 : int
            i1 : int
            i2 : int
            i3 : int
            i4 : int
            i5 : int
            i6 : int
            i7 : int
        }

        member p.toCoord (d : Domain8D) =
            {
                x0 = d.d0.points.value[p.i0]
                x1 = d.d1.points.value[p.i1]
                x2 = d.d2.points.value[p.i2]
                x3 = d.d3.points.value[p.i3]
                x4 = d.d4.points.value[p.i4]
                x5 = d.d5.points.value[p.i5]
                x6 = d.d6.points.value[p.i6]
                x7 = d.d7.points.value[p.i7]
            }

        member p.boundaryCount (d : int) =
            (if p.i0 = 0 || p.i0 = d - 1 then 1 else 0) +
            (if p.i1 = 0 || p.i1 = d - 1 then 1 else 0) +
            (if p.i2 = 0 || p.i2 = d - 1 then 1 else 0) +
            (if p.i3 = 0 || p.i3 = d - 1 then 1 else 0) +
            (if p.i4 = 0 || p.i4 = d - 1 then 1 else 0) +
            (if p.i5 = 0 || p.i5 = d - 1 then 1 else 0) +
            (if p.i6 = 0 || p.i6 = d - 1 then 1 else 0) +
            (if p.i7 = 0 || p.i7 = d - 1 then 1 else 0)


    let arithmeticOperations1D =
        {
            add = (+)
            subtract = (-)
            multiply = (*)
            divide = (/)
            dot = Coord1D.dot
            multiplyByDouble = (.*)
            sqrt = _.sqrt()
            zero = Coord1D.Zero
            one = Coord1D.One
            toArray = (fun (a : Coord1D) -> [| a.x0 |]) |> Some
        }


    let conversionParameters1D d : ConversionParameters<Point1D, Coord1D> =
        {
            arithmetic = arithmeticOperations1D
            projector = fun (p : Point1D) -> p.toCoord d
        }


    let arithmeticOperations2D =
        {
            add = (+)
            subtract = (-)
            multiply = (*)
            divide = (/)
            dot = Coord2D.dot
            multiplyByDouble = (.*)
            sqrt = _.sqrt()
            zero = Coord2D.Zero
            one = Coord2D.One
            toArray = (fun (a : Coord2D) -> [| a.x0; a.x1 |]) |> Some
        }


    let conversionParameters2D d : ConversionParameters<Point2D, Coord2D> =
        {
            arithmetic = arithmeticOperations2D
            projector = fun (p : Point2D) -> p.toCoord d
        }


    let arithmeticOperations3D =
        {
            add = (+)
            subtract = (-)
            multiply = (*)
            divide = (/)
            dot = Coord3D.dot
            multiplyByDouble = (.*)
            sqrt = _.sqrt()
            zero = Coord3D.Zero
            one = Coord3D.One
            toArray = (fun (a : Coord3D) -> [| a.x0; a.x1; a.x2 |]) |> Some
        }


    let conversionParameters3D d : ConversionParameters<Point3D, Coord3D> =
        {
            arithmetic = arithmeticOperations3D
            projector = fun (p : Point3D) -> p.toCoord d
        }


    let arithmeticOperations4D =
        {
            add = (+)
            subtract = (-)
            multiply = (*)
            divide = (/)
            dot = Coord4D.dot
            multiplyByDouble = (.*)
            sqrt = _.sqrt()
            zero = Coord4D.Zero
            one = Coord4D.One
            toArray = (fun (a : Coord4D) -> [| a.x0; a.x1; a.x2; a.x3 |]) |> Some
        }


    let conversionParameters4D d : ConversionParameters<Point4D, Coord4D> =
        {
            arithmetic = arithmeticOperations4D
            projector = fun (p : Point4D) -> p.toCoord d
        }


    let arithmeticOperations5D =
        {
            add = (+)
            subtract = (-)
            multiply = (*)
            divide = (/)
            dot = Coord5D.dot
            multiplyByDouble = (.*)
            sqrt = _.sqrt()
            zero = Coord5D.Zero
            one = Coord5D.One
            toArray = (fun (a : Coord5D) -> [| a.x0; a.x1; a.x2; a.x3; a.x4 |]) |> Some
        }


    let conversionParameters5D d : ConversionParameters<Point5D, Coord5D> =
        {
            arithmetic = arithmeticOperations5D
            projector = fun (p : Point5D) -> p.toCoord d
        }


    let arithmeticOperations6D =
        {
            add = (+)
            subtract = (-)
            multiply = (*)
            divide = (/)
            dot = Coord6D.dot
            multiplyByDouble = (.*)
            sqrt = _.sqrt()
            zero = Coord6D.Zero
            one = Coord6D.One
            toArray = (fun (a : Coord6D) -> [| a.x0; a.x1; a.x2; a.x3; a.x4; a.x5 |]) |> Some
        }


    let conversionParameters6D d : ConversionParameters<Point6D, Coord6D> =
        {
            arithmetic = arithmeticOperations6D
            projector = fun (p : Point6D) -> p.toCoord d
        }


    let arithmeticOperations7D =
        {
            add = (+)
            subtract = (-)
            multiply = (*)
            divide = (/)
            dot = Coord7D.dot
            multiplyByDouble = (.*)
            sqrt = _.sqrt()
            zero = Coord7D.Zero
            one = Coord7D.One
            toArray = (fun (a : Coord7D) -> [| a.x0; a.x1; a.x2; a.x3; a.x4; a.x5; a.x6 |]) |> Some
        }


    let conversionParameters7D d : ConversionParameters<Point7D, Coord7D> =
        {
            arithmetic = arithmeticOperations7D
            projector = fun (p : Point7D) -> p.toCoord d
        }


    let arithmeticOperations8D =
        {
            add = (+)
            subtract = (-)
            multiply = (*)
            divide = (/)
            dot = Coord8D.dot
            multiplyByDouble = (.*)
            sqrt = _.sqrt()
            zero = Coord8D.Zero
            one = Coord8D.One
            toArray = (fun (a : Coord8D) -> [| a.x0; a.x1; a.x2; a.x3; a.x4; a.x5; a.x6; a.x7 |]) |> Some
        }


    let conversionParameters8D d : ConversionParameters<Point8D, Coord8D> =
        {
            arithmetic = arithmeticOperations8D
            projector = fun (p : Point8D) -> p.toCoord d
        }


    type DomainParams =
        {
            domainIntervals : DomainIntervals
            domainRange : DomainRange
        }

        member dd.domain() = Domain.create (dd.domainIntervals, dd.domainRange)

module ZeroThreshold =
    let defaultValue = Primitives.ZeroThreshold 1.0e-05
