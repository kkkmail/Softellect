namespace Softellect.Math

open FSharp.Collections
open System
open Softellect.Math.Primitives

module Sparse1D =

    /// Representation of non-zero value in a sparse array.
    type SparseValue<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                     and ^T: (static member ( + ) : ^T * ^T -> ^T)
                     and ^T: (static member ( - ) : ^T * ^T -> ^T)
                     and ^T: (static member Zero : ^T)
                     and ^T: equality
                     and ^T: comparison> =
        {
            i : int
            value1D : 'T
        }


    // [<RequireQualifiedAccess>]
    type SparseArray<'T when ^T: (static member ( * ) : ^T * ^T -> ^T)
                     and ^T: (static member ( + ) : ^T * ^T -> ^T)
                     and ^T: (static member ( - ) : ^T * ^T -> ^T)
                     and ^T: (static member Zero : ^T)
                     and ^T: equality
                     and ^T: comparison> =
        {
            xValues : SparseValue<'T>[]
            xMap : Lazy<Map<int, 'T>>
        }

        member inline r.value = r.xValues
        member inline r.total() = r.value |> Array.map _.value1D |> Array.sum

        static member inline private createLookupMap (values: SparseValue<'T>[]) =
            values
            |> Array.map (fun v -> v.i, v.value1D)
            |> Map.ofArray

        member inline a.tryFind i = a.xMap.Value.TryFind i

        static member inline create x =
            {
                xValues = x
                xMap = new Lazy<Map<int, 'T>>(fun () -> SparseArray.createLookupMap x)
            }

        static member inline createAbove (z : ZeroThreshold<'T>) (v : 'T[]) =
            let x =
                v
                |> Array.mapi (fun i e -> if e >= z.value then Some { i = i; value1D = e } else None)
                |> Array.choose id

            {
                xValues = x
                xMap = new Lazy<Map<int, 'T>>(fun () -> SparseArray.createLookupMap x)
            }

        static member inline (*) (a : SparseArray<'U>, b : 'U) : SparseArray<'U> =
            a.xValues |> Array.map (fun e -> e * b) |> SparseArray.create

        static member inline (*) (a : 'U, b : SparseArray<'U>) = b * a

        static member inline (*) (a : SparseArray<'U>, b : SparseArray<'U>) : SparseArray<'U> =
            failwith ""


    type Domain
        with

        member d.integralValue (v : SparseValue<double>) =
            let xSize = d.noOfIntervals.value

            match v.i = 0, v.i = xSize with
            | true, false -> 0.5 * v.value1D
            | false, true -> 0.5 * v.value1D
            | _ -> v.value1D

        member d.integrateValues (v : SparseArray<double>) =
            let retVal = v.value |> Array.map d.integralValue |> Array.sum |> d.normalize
            retVal
