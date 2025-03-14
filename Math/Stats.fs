namespace Softellect.Math

open Softellect.Math.Primitives
open Softellect.Math.Sparse
open System

/// Module containing statistical functions for SparseArray types
module Stats =
    let x = 1


    // /// Extension methods for SparseArray<Point2D, int64>
    // type SparseArray<Point2D, int64> with
    //
    //     /// Calculate kth raw statistical moment for the SparseArray using the given domain
    //     /// Returns a vector of moments (one per coordinate dimension)
    //     member array.moment (d : Domain2D) (k : int) : Coord2D =
    //
    //         // Get all values and frequencies
    //         let valuesWithFreq = array.values
    //
    //         // Calculate total count (sum of all frequencies)
    //         let totalCount =
    //             valuesWithFreq
    //             |> Array.sumBy (fun sv -> sv.value)
    //             |> float
    //
    //         if totalCount = 0.0 then
    //             // Return zero moments if array is empty
    //             { x0 = 0.0; x1 = 0.0 }
    //         else
    //             // Calculate raw kth moment: E[X^k]
    //             let kthMoment =
    //                 valuesWithFreq
    //                 |> Array.fold (fun (sumX0, sumX1) sv ->
    //                     let point = sv.x
    //                     let freq = float sv.value
    //                     let coordX0 = d.d0.points[point.i0]
    //                     let coordX1 = d.d1.points[point.i1]
    //
    //                     // Calculate x^k for each dimension
    //                     let x0Pow = Math.Pow(coordX0, float k)
    //                     let x1Pow = Math.Pow(coordX1, float k)
    //
    //                     (sumX0 + x0Pow * freq, sumX1 + x1Pow * freq)
    //                 ) (0.0, 0.0)
    //                 |> fun (sumX0, sumX1) -> (sumX0 / totalCount, sumX1 / totalCount)
    //
    //             { x0 = fst kthMoment; x1 = snd kthMoment }
