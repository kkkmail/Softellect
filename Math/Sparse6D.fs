namespace Softellect.Math

open FSharp.Collections
open MathNet.Numerics.Distributions
open System
open Softellect.Math.Primitives

module Sparse6D =

    let x = 1

    // /// Representation of non-zero value in a sparse 6D array.
    // type SparseValue6D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                      and ^T: (static member Zero : ^T)
    //                      and ^T: equality> =
    //     {
    //         i : int
    //         j : int
    //         k : int
    //         i1 : int
    //         j1 : int
    //         k1 : int
    //         value6D : 'T
    //     }
    //
    //
    // /// A 6D representation of 6D sparse tensor where the first three indexes are full ([][][] is used)
    // /// and the last two are in a SparseArray3D.
    // type SparseArray6D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                      and ^T: (static member Zero : ^T)
    //                      and ^T: equality> =
    //     | SparseArray6D of SparseArray3D<'T>[][][]
    //
    //     member inline r.value = let (SparseArray6D v) = r in v
    //
    //
    // /// Performs a Cartesian multiplication of two 1D sparse arrays to obtain a 2D sparse array.
    // let inline cartesianMultiply<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                      and ^T: (static member Zero : ^T)
    //                      and ^T: equality> (a : SparseArray<'T>) (b : SparseArray<'T>) : SparseArray2D<'T> =
    //     let bValue = b.value
    //     a.value |> Array.map (fun e -> bValue |> Array.map (fun f -> { i = e.i; j = f.i; value2D = e.value1D * f.value1D })) |> Array.concat |> SparseArray2D.create
