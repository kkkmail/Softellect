namespace Softellect.Math

open System.Collections.Generic
open FSharp.Collections
open Softellect.Math.Sparse1D

module Sparse2D =

    /// Representation of non-zero value in a sparse 2D array.
    type SparseValue2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                     and ^T: (static member ( + ) : ^T * ^T -> ^T)
                     and ^T: (static member ( - ) : ^T * ^T -> ^T)
                     and ^T: (static member Zero : ^T)
                     and ^T: equality
                     and ^T: comparison> =
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


    // =================================================================================================================


    // type InseparableSparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                  and ^T: (static member Zero : ^T)
    //                  and ^T: equality
    //                  and ^T: comparison> =
    //     {
    //         xyValues : SparseValue2D<'T>[]
    //         xyMap : Lazy<Map<int * int, 'T>>
    //     }
    //
    //     member inline a.tryFind i j = a.xyMap.Value.TryFind(i, j)
    //
    //     static member inline private createLookupMap (values: SparseValue2D<'T>[]) =
    //         values
    //         |> Array.map (fun v -> (v.i, v.j), v.value2D)
    //         |> Map.ofArray
    //
    //     static member inline create v =
    //         {
    //             xyValues = v
    //             xyMap = new Lazy<Map<int * int, 'T>>(fun () -> InseparableSparseArray2D.createLookupMap v)
    //         }
    //
    //
    // type SeparableSparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                  and ^T: (static member Zero : ^T)
    //                  and ^T: equality
    //                  and ^T: comparison> =
    //     {
    //         xValues : SparseValue<'T>[]
    //         yValues : SparseValue<'T>[]
    //         xMap : Lazy<Map<int, 'T>>
    //         yMap : Lazy<Map<int, 'T>>
    //     }
    //
    //     static member inline private createLookupMap (values: SparseValue<'T>[]) =
    //         values
    //         |> Array.map (fun v -> v.i, v.value1D)
    //         |> Map.ofArray
    //
    //     member inline a.tryFind i j =
    //         match a.xMap.Value.TryFind i, a.yMap.Value.TryFind j with
    //         | Some x, Some y -> x * y |> Some
    //         | _ -> None
    //
    //     static member inline create xValues yValues =
    //         {
    //             xValues = xValues
    //             yValues = yValues
    //             xMap = new Lazy<Map<int, 'T>>(fun () -> SeparableSparseArray2D.createLookupMap xValues)
    //             yMap = new Lazy<Map<int, 'T>>(fun () -> SeparableSparseArray2D.createLookupMap yValues)
    //         }
    //
    //
    // /// SparseArray2D with an internal lookup map for performance
    // /// See: https://github.com/dotnet/fsharp/issues/3302 for (*) operator.
    // // [<RequireQualifiedAccess>]
    // type SparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                  and ^T: (static member Zero : ^T)
    //                  and ^T: equality
    //                  and ^T: comparison> =
    //     | InseparableSparseArr2D of InseparableSparseArray2D<'T>
    //     | SeparableSparseArr2D of SeparableSparseArray2D<'T>
    //
    //     // /// Access the raw array of sparse values
    //     // member inline r.value = let (SparseArray2D(v, _)) = r in v
    //
    //     /// Generate the complete array of sparse values
    //     member inline r.getValues() =
    //         match r with
    //         | InseparableSparseArr2D a -> a.xyValues |> Seq.ofArray
    //         | SeparableSparseArr2D s ->
    //             seq {
    //                 for x in s.xValues do
    //                     for y in s.yValues do
    //                         yield {
    //                             i = x.i
    //                             j = y.i
    //                             value2D = x.value1D * y.value1D
    //                         }
    //             }
    //
    //     /// Access the internal lookup map
    //     member inline r.tryFind i j =
    //         match r with
    //         | InseparableSparseArr2D a -> a.tryFind i j
    //         | SeparableSparseArr2D a -> a.tryFind i j
    //
    //     /// Create a SparseArray2D from an array of SparseValue2D
    //     static member inline create (values: SparseValue2D<'T>[]) : SparseArray2D<'T> =
    //         InseparableSparseArray2D.create values |> InseparableSparseArr2D
    //
    //     /// Create a separable SparseArray2D from two arrays of SparseValue
    //     static member inline create (xValues: SparseValue<'T>[], yValues: SparseValue<'T>[]) : SparseArray2D<'T> =
    //         SeparableSparseArray2D.create xValues yValues |> SeparableSparseArr2D
    //
    //     // ================================================================
    //
    //
    //
    //
    //     /// Element-wise addition of two SparseArray2D instances
    //     static member inline (+) (a: SparseArray2D<'T>, b: SparseArray2D<'T>): SparseArray2D<'T> =
    //         match a, b with
    //         | SeparableSparseArr2D sa, SeparableSparseArr2D sb ->
    //             // For separable + separable
    //             // Addition of separable arrays always produces an inseparable result
    //             let allIndices =
    //                 seq {
    //                     for x in sa.xValues do
    //                         for y in sa.yValues do
    //                             yield (x.i, y.i)
    //                     for x in sb.xValues do
    //                         for y in sb.yValues do
    //                             yield (x.i, y.i)
    //                 } |> Set.ofSeq
    //
    //             let resultValues =
    //                 allIndices
    //                 |> Set.toArray
    //                 |> Array.choose (fun (i, j) ->
    //                     let aVal = a.tryFind i j
    //                     let bVal = b.tryFind i j
    //
    //                     match aVal, bVal with
    //                     | Some av, Some bv ->
    //                         let sum = av + bv
    //                         let zero = Unchecked.defaultof<'T>
    //                         if sum = zero then None
    //                         else Some { i = i; j = j; value2D = sum }
    //                     | Some av, None -> Some { i = i; j = j; value2D = av }
    //                     | None, Some bv -> Some { i = i; j = j; value2D = bv }
    //                     | None, None -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //         | SeparableSparseArr2D sa, InseparableSparseArr2D ib ->
    //             // For separable + inseparable
    //             let allIndices =
    //                 seq {
    //                     for x in sa.xValues do
    //                         for y in sa.yValues do
    //                             yield (x.i, y.i)
    //                     yield! ib.xyValues |> Array.map (fun v -> (v.i, v.j))
    //                 } |> Set.ofSeq
    //
    //             let resultValues =
    //                 allIndices
    //                 |> Set.toArray
    //                 |> Array.choose (fun (i, j) ->
    //                     let aVal = a.tryFind i j
    //                     let bVal = b.tryFind i j
    //
    //                     match aVal, bVal with
    //                     | Some av, Some bv ->
    //                         let sum = av + bv
    //                         let zero = Unchecked.defaultof<'T>
    //                         if sum = zero then None
    //                         else Some { i = i; j = j; value2D = sum }
    //                     | Some av, None -> Some { i = i; j = j; value2D = av }
    //                     | None, Some bv -> Some { i = i; j = j; value2D = bv }
    //                     | None, None -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //         | InseparableSparseArr2D ia, SeparableSparseArr2D sb ->
    //             // For inseparable + separable
    //             let allIndices =
    //                 seq {
    //                     yield! ia.xyValues |> Array.map (fun v -> (v.i, v.j))
    //                     for x in sb.xValues do
    //                         for y in sb.yValues do
    //                             yield (x.i, y.i)
    //                 } |> Set.ofSeq
    //
    //             let resultValues =
    //                 allIndices
    //                 |> Set.toArray
    //                 |> Array.choose (fun (i, j) ->
    //                     let aVal = a.tryFind i j
    //                     let bVal = b.tryFind i j
    //
    //                     match aVal, bVal with
    //                     | Some av, Some bv ->
    //                         let sum = av + bv
    //                         let zero = Unchecked.defaultof<'T>
    //                         if sum = zero then None
    //                         else Some { i = i; j = j; value2D = sum }
    //                     | Some av, None -> Some { i = i; j = j; value2D = av }
    //                     | None, Some bv -> Some { i = i; j = j; value2D = bv }
    //                     | None, None -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //         | InseparableSparseArr2D ia, InseparableSparseArr2D ib ->
    //             // For inseparable + inseparable
    //             let allIndices =
    //                 Set.union
    //                     (ia.xyValues |> Array.map (fun v -> (v.i, v.j)) |> Set.ofArray)
    //                     (ib.xyValues |> Array.map (fun v -> (v.i, v.j)) |> Set.ofArray)
    //
    //             let resultValues =
    //                 allIndices
    //                 |> Set.toArray
    //                 |> Array.choose (fun (i, j) ->
    //                     let aVal = ia.tryFind i j
    //                     let bVal = ib.tryFind i j
    //
    //                     match aVal, bVal with
    //                     | Some av, Some bv ->
    //                         let sum = av + bv
    //                         let zero = Unchecked.defaultof<'T>
    //                         if sum = zero then None
    //                         else Some { i = i; j = j; value2D = sum }
    //                     | Some av, None -> Some { i = i; j = j; value2D = av }
    //                     | None, Some bv -> Some { i = i; j = j; value2D = bv }
    //                     | None, None -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //     /// Element-wise subtraction of two SparseArray2D instances
    //     static member inline (-) (a: SparseArray2D<'T>, b: SparseArray2D<'T>): SparseArray2D<'T> =
    //         match a, b with
    //         | SeparableSparseArr2D sa, SeparableSparseArr2D sb ->
    //             // For separable - separable
    //             let allIndices =
    //                 seq {
    //                     for x in sa.xValues do
    //                         for y in sa.yValues do
    //                             yield (x.i, y.i)
    //                     for x in sb.xValues do
    //                         for y in sb.yValues do
    //                             yield (x.i, y.i)
    //                 } |> Set.ofSeq
    //
    //             let resultValues =
    //                 allIndices
    //                 |> Set.toArray
    //                 |> Array.choose (fun (i, j) ->
    //                     let aVal = a.tryFind i j
    //                     let bVal = b.tryFind i j
    //
    //                     match aVal, bVal with
    //                     | Some av, Some bv ->
    //                         let diff = av - bv
    //                         let zero = Unchecked.defaultof<'T>
    //                         if diff = zero then None
    //                         else Some { i = i; j = j; value2D = diff }
    //                     | Some av, None -> Some { i = i; j = j; value2D = av }
    //                     | None, Some bv ->
    //                         // Negate bv for subtraction
    //                         let negB = Unchecked.defaultof<'T> - bv
    //                         Some { i = i; j = j; value2D = negB }
    //                     | None, None -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //         | SeparableSparseArr2D sa, InseparableSparseArr2D ib ->
    //             // For separable - inseparable
    //             let allIndices =
    //                 seq {
    //                     for x in sa.xValues do
    //                         for y in sa.yValues do
    //                             yield (x.i, y.i)
    //                     yield! ib.xyValues |> Array.map (fun v -> (v.i, v.j))
    //                 } |> Set.ofSeq
    //
    //             let resultValues =
    //                 allIndices
    //                 |> Set.toArray
    //                 |> Array.choose (fun (i, j) ->
    //                     let aVal = a.tryFind i j
    //                     let bVal = b.tryFind i j
    //
    //                     match aVal, bVal with
    //                     | Some av, Some bv ->
    //                         let diff = av - bv
    //                         let zero = Unchecked.defaultof<'T>
    //                         if diff = zero then None
    //                         else Some { i = i; j = j; value2D = diff }
    //                     | Some av, None -> Some { i = i; j = j; value2D = av }
    //                     | None, Some bv ->
    //                         // Negate bv for subtraction
    //                         let negB = Unchecked.defaultof<'T> - bv
    //                         Some { i = i; j = j; value2D = negB }
    //                     | None, None -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //         | InseparableSparseArr2D ia, SeparableSparseArr2D sb ->
    //             // For inseparable - separable
    //             let allIndices =
    //                 seq {
    //                     yield! ia.xyValues |> Array.map (fun v -> (v.i, v.j))
    //                     for x in sb.xValues do
    //                         for y in sb.yValues do
    //                             yield (x.i, y.i)
    //                 } |> Set.ofSeq
    //
    //             let resultValues =
    //                 allIndices
    //                 |> Set.toArray
    //                 |> Array.choose (fun (i, j) ->
    //                     let aVal = a.tryFind i j
    //                     let bVal = b.tryFind i j
    //
    //                     match aVal, bVal with
    //                     | Some av, Some bv ->
    //                         let diff = av - bv
    //                         let zero = Unchecked.defaultof<'T>
    //                         if diff = zero then None
    //                         else Some { i = i; j = j; value2D = diff }
    //                     | Some av, None -> Some { i = i; j = j; value2D = av }
    //                     | None, Some bv ->
    //                         // Negate bv for subtraction
    //                         let negB = Unchecked.defaultof<'T> - bv
    //                         Some { i = i; j = j; value2D = negB }
    //                     | None, None -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //         | InseparableSparseArr2D ia, InseparableSparseArr2D ib ->
    //             // For inseparable - inseparable
    //             let allIndices =
    //                 Set.union
    //                     (ia.xyValues |> Array.map (fun v -> (v.i, v.j)) |> Set.ofArray)
    //                     (ib.xyValues |> Array.map (fun v -> (v.i, v.j)) |> Set.ofArray)
    //
    //             let resultValues =
    //                 allIndices
    //                 |> Set.toArray
    //                 |> Array.choose (fun (i, j) ->
    //                     let aVal = ia.tryFind i j
    //                     let bVal = ib.tryFind i j
    //
    //                     match aVal, bVal with
    //                     | Some av, Some bv ->
    //                         let diff = av - bv
    //                         let zero = Unchecked.defaultof<'T>
    //                         if diff = zero then None
    //                         else Some { i = i; j = j; value2D = diff }
    //                     | Some av, None -> Some { i = i; j = j; value2D = av }
    //                     | None, Some bv ->
    //                         // Negate bv for subtraction
    //                         let negB = Unchecked.defaultof<'T> - bv
    //                         Some { i = i; j = j; value2D = negB }
    //                     | None, None -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //     /// Element-wise multiplication of two SparseArray2D instances
    //     static member inline (*) (a: SparseArray2D<'T>, b: SparseArray2D<'T>): SparseArray2D<'T> =
    //         match a, b with
    //         | SeparableSparseArr2D sa, SeparableSparseArr2D sb ->
    //             // For separable * separable, the result is separable
    //             // We multiply the respective x and y components
    //             let resultXValues =
    //                 [|
    //                     for xA in sa.xValues do
    //                         for xB in sb.xValues do
    //                             if xA.i = xB.i then
    //                                 let product = xA.value1D * xB.value1D
    //                                 let zero = Unchecked.defaultof<'T>
    //                                 if product <> zero then
    //                                     yield { i = xA.i; value1D = product }
    //                 |]
    //
    //             let resultYValues =
    //                 [|
    //                     for yA in sa.yValues do
    //                         for yB in sb.yValues do
    //                             if yA.i = yB.i then
    //                                 let product = yA.value1D * yB.value1D
    //                                 let zero = Unchecked.defaultof<'T>
    //                                 if product <> zero then
    //                                     yield { i = yA.i; value1D = product }
    //                 |]
    //
    //             SparseArray2D.create (resultXValues, resultYValues)
    //
    //         | SeparableSparseArr2D sa, InseparableSparseArr2D ib ->
    //             // For separable * inseparable
    //             let resultValues =
    //                 ib.xyValues
    //                 |> Array.choose (fun bVal ->
    //                     match sa.xMap.Value.TryFind bVal.i, sa.yMap.Value.TryFind bVal.j with
    //                     | Some xv, Some yv ->
    //                         let saValue = xv * yv
    //                         let product = saValue * bVal.value2D
    //                         let zero = Unchecked.defaultof<'T>
    //                         if product = zero then None
    //                         else Some { i = bVal.i; j = bVal.j; value2D = product }
    //                     | _ -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //         | InseparableSparseArr2D ia, SeparableSparseArr2D sb ->
    //             // For inseparable * separable
    //             let resultValues =
    //                 ia.xyValues
    //                 |> Array.choose (fun aVal ->
    //                     match sb.xMap.Value.TryFind aVal.i, sb.yMap.Value.TryFind aVal.j with
    //                     | Some xv, Some yv ->
    //                         let sbValue = xv * yv
    //                         let product = aVal.value2D * sbValue
    //                         let zero = Unchecked.defaultof<'T>
    //                         if product = zero then None
    //                         else Some { i = aVal.i; j = aVal.j; value2D = product }
    //                     | _ -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //         | InseparableSparseArr2D ia, InseparableSparseArr2D ib ->
    //             // For inseparable * inseparable
    //             let resultValues =
    //                 ia.xyValues
    //                 |> Array.choose (fun aVal ->
    //                     match ib.tryFind aVal.i aVal.j with
    //                     | Some bVal ->
    //                         let product = aVal.value2D * bVal
    //                         let zero = Unchecked.defaultof<'T>
    //                         if product = zero then None
    //                         else Some { i = aVal.i; j = aVal.j; value2D = product }
    //                     | None -> None
    //                 )
    //
    //             SparseArray2D.create resultValues
    //
    //
    //
    //
    //     // ================================================================
    //
    //     /// Multiplies a 2D sparse array by a scalar value.
    //     /// Returns a 2D sparse array.
    //     static member inline (*) (a : SparseArray2D<'U>, b : 'U) : SparseArray2D<'U> =
    //         match a with
    //         | InseparableSparseArr2D ia ->
    //             let v =
    //                 ia.xyValues
    //                 |> Array.map (fun e -> { e with value2D = e.value2D * b })
    //                 |> InseparableSparseArray2D.create
    //
    //             InseparableSparseArr2D v
    //         | SeparableSparseArr2D sa ->
    //             let xValues =
    //                 sa.xValues
    //                 |> Array.map (fun e -> { e with value1D = e.value1D * b })
    //
    //             SeparableSparseArray2D.create xValues sa.yValues |> SeparableSparseArr2D
    //
    //     /// Multiplies a 2D sparse array by a scalar value.
    //     /// Returns a 2D sparse array.
    //     static member inline (*) (a : 'U, b : SparseArray2D<'U>) : SparseArray2D<'U> = b * a
    //
    //     /// Multiplies a 2D sparse array by a 2D array (LinearMatrix) element by element.
    //     /// This is NOT a matrix multiplication.
    //     /// Returns a 2D sparse array.
    //     static member inline (*) (a : SparseArray2D<'T>, b : LinearMatrix<'T>) : SparseArray2D<'T> =
    //         // let v =
    //         //     a.value
    //         //     |> Array.map (fun e -> { e with value2D = e.value2D * (b.getValue e.i e.j) })
    //         //     |> SparseArray2D.create
    //         //
    //         // v
    //         failwith ""
    //
    //     /// Multiplies a 2D sparse array by a 2D array (Matrix) element by element.
    //     /// This is NOT a matrix multiplication.
    //     /// Returns a 2D sparse array.
    //     static member inline (*) (a : SparseArray2D<'T>, b : Matrix<'T>) : SparseArray2D<'T> =
    //         // let v =
    //         //     a.value
    //         //     |> Array.map (fun e -> { e with value2D = e.value2D * b.value[e.i][e.j] })
    //         //     |> SparseArray2D.create
    //         //
    //         // v
    //         failwith ""
    //
    //     static member inline createAbove z v =
    //         v
    //         |> Array.mapi (fun i e -> e |> Array.mapi (fun j v -> if v >= z then Some { i = i; j = j; value2D = v } else None))
    //         |> Array.concat
    //         |> Array.choose id
    //         |> SparseArray2D.create


    // =================================================================================================================


    // /// A static representation of 2D sparse array.
    // type StaticSparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                  and ^T: (static member Zero : ^T)
    //                  and ^T: equality
    //                  and ^T: comparison> =
    //     | StaticSparseArray2D of SparseValue2D<'T>[]
    //
    //     member inline r.value = let (StaticSparseArray2D v) = r in v
    //
    //     /// Multiplies a 2D static sparse array by a scalar value.
    //     /// Returns a 2D static sparse array.
    //     static member inline (*) (a : StaticSparseArray2D<'U>, b : 'U) : StaticSparseArray2D<'U> =
    //         let v =
    //             a.value
    //             |> Array.map (fun e -> e * b)
    //             |> StaticSparseArray2D
    //
    //         v
    //
    //
    // /// A dynamic representation of 2D sparse array.
    // type DynamicSparseArrayData2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                  and ^T: (static member Zero : ^T)
    //                  and ^T: equality
    //                  and ^T: comparison> =
    //     {
    //         getData : int -> SparseArray<'T>
    //         xLength : int
    //     }
    //
    //
    // type DynamicSparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                  and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                  and ^T: (static member Zero : ^T)
    //                  and ^T: equality
    //                  and ^T: comparison> =
    //     | DynamicSparseArray2D of DynamicSparseArrayData2D<'T>
    //
    //     member inline private r.value = let (DynamicSparseArray2D v) = r in v
    //     member inline r.invoke = let (DynamicSparseArray2D v) = r in v.getData
    //     member inline r.xLength = let (DynamicSparseArray2D v) = r in v.xLength
    //
    //     // static member inline (*) (a : DynamicSparseArray2D<'T>, b : SparseArray<'T>) : DynamicSparseArray2D<'T> =
    //     //     let originalFunc = a.invoke
    //     //
    //     //     // Create a new function that multiplies the result of the original function by b
    //     //     let newFunc i : SparseArray<'T> =
    //     //         let sparseArray = originalFunc i
    //     //         sparseArray * b
    //     //
    //     //     { a.value with getData = newFunc } |> DynamicSparseArray2D
    //
    //     static member inline (*) (a : DynamicSparseArray2D<'U>, b : 'U) : DynamicSparseArray2D<'U> =
    //         let originalFunc = a.invoke
    //
    //         // Create a new function that multiplies the result of the original function by b
    //         let newFunc i : SparseArray<'U> =
    //             let sparseArray = originalFunc i
    //             sparseArray * b
    //
    //         { a.value with getData = newFunc } |> DynamicSparseArray2D
    //
    //     static member inline (*) (a : 'U, b : DynamicSparseArray2D<'U>) : DynamicSparseArray2D<'U> = b * a


    // =================================================================================================================


    type InseparableSparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                     and ^T: (static member ( + ) : ^T * ^T -> ^T)
                     and ^T: (static member ( - ) : ^T * ^T -> ^T)
                     and ^T: (static member Zero : ^T)
                     and ^T: equality
                     and ^T: comparison> =
        | InseparableSparseArray2D of SparseValue2D<'T>[]

        member inline r.value = let (InseparableSparseArray2D v) = r in v

        // static member inline createLookupMap (values: SparseValue2D<'T>[]) =
        //     values
        //     |> Array.map (fun v -> (v.i, v.j), v.value2D)
        //     |> Map.ofArray
        //
        // static member inline create (v : SparseValue2D<'U>[]) : InseparableSparseArray2D<'U> = InseparableSparseArray2D v


    type SeparableSparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                     and ^T: (static member ( + ) : ^T * ^T -> ^T)
                     and ^T: (static member ( - ) : ^T * ^T -> ^T)
                     and ^T: (static member Zero : ^T)
                     and ^T: equality
                     and ^T: comparison> =
        {
            xValues : SparseValue<'T>[]
            yValues : SparseValue<'T>[]
            // xMap : Lazy<Map<int, 'T>>
            // yMap : Lazy<Map<int, 'T>>
        }

        // static member inline createLookupMap (values: SparseValue<'T>[]) =
        //     values
        //     |> Array.map (fun v -> v.i, v.value1D)
        //     |> Map.ofArray
        //
        // member inline a.tryFind i j =
        //     match a.xMap.Value.TryFind i, a.yMap.Value.TryFind j with
        //     | Some x, Some y -> x * y |> Some
        //     | _ -> None

        static member inline create xValues yValues =
            {
                xValues = xValues
                yValues = yValues
                // xMap = new Lazy<Map<int, 'T>>(fun () -> SeparableSparseArray2D.createLookupMap xValues)
                // yMap = new Lazy<Map<int, 'T>>(fun () -> SeparableSparseArray2D.createLookupMap yValues)
            }


    /// See: https://github.com/dotnet/fsharp/issues/3302 for (*) operator.
    type SparseArray2D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                     and ^T: (static member ( + ) : ^T * ^T -> ^T)
                     and ^T: (static member ( - ) : ^T * ^T -> ^T)
                     and ^T: (static member Zero : ^T)
                     and ^T: equality
                     and ^T: comparison> =
        | InseparableSparseArr2D of InseparableSparseArray2D<'T>
        | SeparableSparseArr2D of SeparableSparseArray2D<'T>

        /// Generate the complete array of sparse values as a seq.
        member inline r.getValues() =
            match r with
            | InseparableSparseArr2D a -> a.value |> Seq.ofArray
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

        member inline r.total() = r.getValues() |> Seq.map _.value2D |> Seq.sum

        member inline r.tryFind (i, j) = failwith ""

        // /// Access the internal lookup map
        // member inline r.tryFind i j =
        //     match r with
        //     | InseparableSparseArr2D a -> a.tryFind i j
        //     | SeparableSparseArr2D a -> a.tryFind i j

        /// Create a SparseArray2D from an array of SparseValue2D
        static member inline create (values: SparseValue2D<'T>[]) : SparseArray2D<'T> =
            InseparableSparseArray2D values |> InseparableSparseArr2D

        /// Create a separable SparseArray2D from two arrays of SparseValue
        static member inline create (xValues: SparseValue<'T>[], yValues: SparseValue<'T>[]) : SparseArray2D<'T> =
            SeparableSparseArray2D<'T>.create xValues yValues |> SeparableSparseArr2D


    /// Extract the key/value pairs from the dictionary into an array of SparseValue2D<'T>
    let inline private toSparseArray2D (dict : Dictionary<int * int, 'T>) =
        let resultArray =
            dict
            |> Seq.map (fun kvp -> { i = fst kvp.Key; j = snd kvp.Key; value2D = kvp.Value })
            |> Seq.toArray

        resultArray |> InseparableSparseArray2D |> InseparableSparseArr2D


    let inline sumSparseArrays (arrays: list<SparseArray2D<'T>>) : SparseArray2D<'T> =
        let dict = Dictionary<int * int, 'T>()

        for values in arrays do
            for v in values.getValues() do
                let key = (v.i, v.j)

                if dict.ContainsKey(key) then dict.[key] <- dict.[key] + v.value2D
                else dict.Add(key, v.value2D)

        // A sum is generally inseparable.
        toSparseArray2D dict


    let inline multiplySparseArrays (arrays: list<SparseArray2D<'T>>) : SparseArray2D<'T> =
        let result = Dictionary<int * int, 'T>()
        let keysToRemove = HashSet<int * int>()

        let g (a : SparseArray2D<'T>) =
            keysToRemove.Clear()

            for key in result.Keys do
                match a.tryFind key with
                | Some value -> result.[key] <- result.[key] * value
                | None -> keysToRemove.Add key |> ignore

            for key in keysToRemove do result.Remove(key) |> ignore

        match arrays with
        | [] -> ()
        | h :: t ->
            for v in h.getValues() do result.Add((v.i, v.j), v.value2D)
            t |> List.map g |> ignore

        toSparseArray2D result


    /// Performs a Cartesian multiplication of two 1D sparse arrays to obtain a 2D sparse array.
    let inline cartesianMultiply<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                     and ^T: (static member ( + ) : ^T * ^T -> ^T)
                     and ^T: (static member ( - ) : ^T * ^T -> ^T)
                     and ^T: (static member Zero : ^T)
                     and ^T: equality
                     and ^T: comparison> (a : SparseArray<'T>) (b : SparseArray<'T>) : SparseArray2D<'T> =
        let bValue = b.value
        a.value |> Array.map (fun e -> bValue |> Array.map (fun f -> { i = e.i; j = f.i; value2D = e.value1D * f.value1D })) |> Array.concat |> SparseArray2D<'T>.create
