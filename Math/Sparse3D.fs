namespace Softellect.Math

open FSharp.Collections
open MathNet.Numerics.Distributions
open System
open Softellect.Math.Primitives

module Sparse3D =

    let x = 1

    // /// Representation of non-zero value in a sparse 3D array.
    // type SparseValue3D<'T> =
    //     {
    //         i : int
    //         j : int
    //         k : int
    //         value3D : 'T
    //     }
    //
    //
    // [<RequireQualifiedAccess>]
    // type SparseArray3D<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( + ) : ^T * ^T -> ^T)
    //                      and ^T: (static member ( - ) : ^T * ^T -> ^T)
    //                      and ^T: (static member Zero : ^T)
    //                      and ^T: equality> =
    //     | SparseArray3D of SparseValue3D<'T>[]
    //
    //     member inline r.value = let (SparseArray3D v) = r in v
    //
    //
