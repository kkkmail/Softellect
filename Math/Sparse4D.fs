namespace Softellect.Math

open FSharp.Collections
open MathNet.Numerics.Distributions
open System
open Softellect.Math.Primitives
open Softellect.Math.Sparse1D
open Softellect.Math.Sparse2D

module Sparse4D =

    let x = 1

    // /// Representation of non-zero value in a sparse 4D array.
    // type SparseValue4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                      and ^T: (static member Zero : ^T)
    //                      and ^T: equality> =
    //     {
    //         i : int
    //         j : int
    //         i1 : int
    //         j1 : int
    //         value4D : 'T
    //     }
    //
    //     /// Uses (i, j) as the first pair of indexes.
    //     static member inline create i j (v : SparseValue2D<'T>) =
    //         {
    //             i = i
    //             j = j
    //             i1 = v.i
    //             j1 = v.j
    //             value4D = v.value2D
    //         }
    //
    //     /// Uses (i, j) as the second pair of indexes.
    //     static member inline createTransposed i j (v : SparseValue2D<'T>) =
    //         {
    //             i = v.i
    //             j = v.j
    //             i1 = i
    //             j1 = j
    //             value4D = v.value2D
    //         }
    //
    //     static member inline createSeq i j (x : SparseArray2D<'T>) =
    //         x.getValues() |> Seq.map (SparseValue4D.create i j)
    //
    //
    // type SparseValueArray4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                      and ^T: (static member Zero : ^T)
    //                      and ^T: equality> =
    //     | SparseValueArray4D of SparseValue4D<'T>[]
    //
    //     member inline r.value = let (SparseValueArray4D v) = r in v
    //
    //
    // /// A static 4D representation of 4D sparse tensor where the first two indexes are full ([][] is used)
    // /// and the last two are in a SparseArray2D.
    // type StaticSparseArray4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                      and ^T: (static member Zero : ^T)
    //                      and ^T: equality> =
    //     | StaticSparseArray4D of SparseArray2D<'T>[][]
    //
    //     member inline r.value = let (StaticSparseArray4D v) = r in v
    //
    //     /// Multiplies a 4D sparse array by a scalar value.
    //     /// Returns a 4D sparse array.
    //     static member inline (*) (a : StaticSparseArray4D<'U>, b : 'U) : StaticSparseArray4D<'U> =
    //         let v =
    //             a.value
    //             |> Array.map (fun e -> e |> Array.map (fun x -> x * b))
    //             |> StaticSparseArray4D
    //
    //         v
    //
    //
    // /// A dynamic 4D representation of 4D sparse tensor where the first two indexes are full
    // /// and the last two are in a SparseArray2D.
    // /// This is suitable for large sparse tensors where instantiation of [][] is not feasible.
    // type DynamicSparseArrayData4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                      and ^T: (static member Zero : ^T)
    //                      and ^T: equality> =
    //     {
    //         getData2D : int -> int -> SparseArray2D<'T>
    //         xLength : int
    //         yLength : int
    //     }
    //
    //
    // type DynamicSparseArray4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                      and ^T: (static member Zero : ^T)
    //                      and ^T: equality> =
    //     | DynamicSparseArray4D of DynamicSparseArrayData4D<'T>
    //
    //     member inline private r.value = let (DynamicSparseArray4D v) = r in v
    //     member inline r.invoke = let (DynamicSparseArray4D v) = r in v.getData2D
    //     member inline r.xLength = let (DynamicSparseArray4D v) = r in v.xLength
    //     member inline r.yLength = let (DynamicSparseArray4D v) = r in v.yLength
    //
    //     static member inline (*) (a : DynamicSparseArray4D<'T>, b : SparseArray2D<'T>) : DynamicSparseArray4D<'T> =
    //         let originalFunc = a.invoke
    //
    //         // Create a new function that multiplies the result of the original function by b
    //         let newFunc i j : SparseArray2D<'T> =
    //             let sparseArray2D = originalFunc i j
    //             sparseArray2D * b
    //
    //         { a.value with getData2D = newFunc } |> DynamicSparseArray4D
    //
    //
    // /// A 4D representation of 4D sparse tensor where the first two indexes are full ([][] is used)
    // /// and the last two are in a SparseArray2D.
    // type SparseArray4D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                      and ^T: (static member Zero : ^T)
    //                      and ^T: equality> =
    //     | StaticSparseArr4D of StaticSparseArray4D<'T>
    //     | DynamicSparseArr4D of DynamicSparseArray4D<'T>
    //
    //     // member inline r.value = let (SparseArray4D v) = r in v
    //
    //     /// Multiplies a 4D sparse array by a 2D array (LinearMatrix) using SECOND pair of indexes in 4D array.
    //     /// This is NOT a matrix multiplication.
    //     /// Returns a 4D sparse array.
    //     static member inline (*) (a : SparseArray4D<'T>, b : LinearMatrix<'T>) : SparseArray4D<'T> =
    //         // let v =
    //         //     a.value
    //         //     |> Array.map (fun e -> e |> Array.map (fun x -> x * b))
    //         //     |> SparseArray4D
    //         //
    //         // v
    //         failwith ""
    //
    //     /// Multiplies a 4D sparse array by a 2D array (Matrix) using SECOND pair of indexes in 4D array.
    //     /// This is NOT a matrix multiplication.
    //     /// Returns a 4D sparse array.
    //     static member inline (*) (a : SparseArray4D<'T>, b : Matrix<'T>) : SparseArray4D<'T> =
    //         match a with
    //         | StaticSparseArr4D s ->
    //             let v =
    //                 s.value
    //                 |> Array.map (fun e -> e |> Array.map (fun x -> x * b))
    //                 |> StaticSparseArray4D
    //                 |> StaticSparseArr4D
    //
    //             v
    //         | DynamicSparseArr4D d ->
    //             failwith ""
    //
    //     /// This is mostly for tests.
    //     member inline a.toSparseValueArray() : SparseValueArray4D<'T> =
    //         match a with
    //         | StaticSparseArr4D s ->
    //             let n1 = s.value.Length
    //             let n2 = s.value[0].Length
    //
    //             let value =
    //                 [| for i in 0..(n1 - 1) -> [| for j in 0..(n2 - 1) -> SparseValue4D.createSeq i j (s.value[i][j]) |] |]
    //                 |> Array.concat
    //                 |> Seq.concat
    //                 |> Seq.toArray
    //                 |> Array.sortBy (fun e -> e.i, e.j, e.i1, e.j1)
    //                 |> SparseValueArray4D
    //
    //             value
    //
    //         | DynamicSparseArr4D d ->
    //             let n1 = d.xLength
    //             let n2 = d.yLength
    //
    //             let value =
    //                 [| for i in 0..(n1 - 1) -> [| for j in 0..(n2 - 1) -> SparseValue4D.createSeq i j (d.invoke i j) |] |]
    //                 |> Array.concat
    //                 |> Seq.concat
    //                 |> Seq.toArray
    //                 |> Array.sortBy (fun e -> e.i, e.j, e.i1, e.j1)
    //                 |> SparseValueArray4D
    //
    //             value
    //
    //
