﻿namespace Softellect.Math

open System
open Softellect.Math.Primitives
open Softellect.Math.Sparse1D
open Softellect.Math.Sparse2D
open Softellect.Math.Sparse4D

module FredholmKernel =

    let private evolveValue (p : PoissonSingleSampler) multiplier e n =
        if n <= 0L then 0L
        else
            let lambda = (double n) * e.value2D * multiplier
            let retVal = if lambda > 0.0 then p.nextPoisson lambda else 0L
            retVal


    type Domain2D
        with

        /// Calculate a value to be used in integral approximation.
        /// Corners carry 0.25 multiplier, edges - 0.5 and the rest - 1.0 (no multiplier).
        member private d.integralValue  (v : SparseValue2D<double>) =
            let xSize = d.xDomain.noOfIntervals.value
            let ySize = d.yDomain.noOfIntervals.value

            match v.i = 0, v.j = 0, v.i = xSize, v.j = ySize with
            | true, true, false, false -> 0.25 * v.value2D
            | true, false, false, true -> 0.25 * v.value2D
            | false, true, true, false -> 0.25 * v.value2D
            | false, false, true, true -> 0.25 * v.value2D

            | true, false, false, false -> 0.5 * v.value2D
            | false, true, false, false -> 0.5 * v.value2D
            | false, false, true, false -> 0.5 * v.value2D
            | false, false, false, true -> 0.5 * v.value2D

            | _ -> v.value2D

        /// Calculates an integral value for a given 2D matrix.
        member private d.integrateValues (a : double[][])=
            let len1 = a.Length - 1
            let len2 = a[0].Length - 1
            let sum = a |> Array.map (fun e -> e |> Array.sum) |> Array.sum
            let edgeSum1 = a[0] |> Array.sum
            let edgeSum2 = a[len1] |> Array.sum
            let edgeSum3 = a |> Array.mapi (fun i _ -> a[i][0]) |> Array.sum
            let edgeSum4 = a |> Array.mapi (fun i _ -> a[i][len2]) |> Array.sum
            let cornerSum = a[0][0] + a[len1][0] + a[0][len2] + a[len1][len2]
            let retVal = (4.0 * sum - 2.0 * (edgeSum1 + edgeSum2 + edgeSum3 + edgeSum4) + cornerSum) / 4.0 |> d.normalize
            retVal

        /// Calculates an integral value for a given 2D sparse array.
        member d.integrateValues (a : SparseArray2D<double>) =
            let retVal = a.getValues() |> Seq.map d.integralValue |> Seq.sum |> d.normalize
            retVal

        /// Calculates an integral value for a multiplication of a given 2D sparse array and linear matrix.
        member private d.integrateValues (a : SparseArray2D<double>, b : LinearMatrix<double>) =
            let bValue = b.getValue
            let retVal = a.getValues() |> Seq.map (fun e -> (d.integralValue e) * (bValue e.i e.j)) |> Seq.sum |> d.normalize
            retVal

        /// Calculates an integral value for a multiplication of a given 2D sparse array and 2d sparse array.
        member private d.integrateValues (a : SparseArray2D<double>, b : SparseArray2D<double>) =
            let ab = multiplySparseArrays [ a; b ]
            let retVal = ab.getValues() |> Seq.map d.integralValue |> Seq.sum |> d.normalize
            retVal

        /// Performs a Poisson "evolution" for a given "point".
        /// This is essentially a tau-leaping algorithm.
        /// The multiplier controls the step of the evolution.
        /// The values are considered non-negative and so all negative values are treated as zero.
        member private d.evolve(p : PoissonSingleSampler, multiplier : double, a : SparseArray2D<double>, b : Matrix<int64>) =
            let g (e : SparseValue2D<double>) = b.value[e.i][e.j] |> evolveValue p multiplier e
            let sum = a.getValues() |> Seq.map g |> Seq.sum
            sum

        /// Performs a Poisson "evolution" for a given "point".
        /// The values are considered non-negative and so all negative values are treated as zero.
        /// It is the same as above but for a linear matrix.
        member private d.evolve(p : PoissonSingleSampler, a : SparseArray2D<double>, b : LinearMatrix<int64>) =
            let bValue = b.getValue
            let g (e : SparseValue2D<double>) =  bValue e.i e.j |> evolveValue p 1.0 e
            let sum = a.getValues() |> Seq.map g |> Seq.sum
            sum

        /// Performs a Poisson "evolution" for a given "point".
        /// This is essentially a tau-leaping algorithm.
        /// The multiplier controls the step of the evolution.
        /// The values are considered non-negative and so all negative values are treated as zero.
        member private d.evolve(p : PoissonSingleSampler, multiplier : double, a : SparseArray2D<double>, b : SparseArray2D<int64>) =
            let g (e : SparseValue2D<double>) = b.tryFind (e.i, e.j) |> Option.map (evolveValue p multiplier e) |> Option.defaultValue 0L
            let sum = a.getValues() |> Seq.map g |> Seq.sum
            sum

        /// Calculates an integral value for a given 2D matrix.
        member d.integrateValues (a : Matrix<double>) = d.integrateValues a.value

        /// Calculates an integral value for a given linear matrix.
        member d.integrateValues (a : LinearMatrix<double>) =
            let len1 = a.d1 - 1
            let len2 = a.d2 - 1
            let r1 = a.d1Range
            let r2 = a.d2Range

            let sum = r1 |> Array.map (fun i -> r2 |> Array.map (fun j -> a.getValue i j) |> Array.sum) |> Array.sum
            let edgeSum1 = r2 |> Array.map (fun j -> a.getValue 0 j) |> Array.sum
            let edgeSum2 = r2 |> Array.map (fun j -> a.getValue len1 j) |> Array.sum
            let edgeSum3 = r1 |> Array.map (fun i -> a.getValue i 0) |> Array.sum
            let edgeSum4 = r1 |> Array.map (fun i -> a.getValue i len2) |> Array.sum
            let cornerSum = (a.getValue 0 0) + (a.getValue len1 0) + (a.getValue 0 len2) + (a.getValue len1 len2)
            let retVal = (4.0 * sum - 2.0 * (edgeSum1 + edgeSum2 + edgeSum3 + edgeSum4) + cornerSum) / 4.0 |> d.normalize
            retVal

        // member private d.integrateValues (a : double[][]) = a |> Array.map (fun e -> e |> Array.sum) |> Array.sum |> d.normalize
        // member private d.integrateValues (a : SparseArray2D<double>) = a.value |> Array.map (fun e -> e.value2D) |> Array.sum |> d.normalize
        //
        // member private d.integrateValues (a : SparseArray2D<double>, b : LinearMatrix<double>) =
        //     let bValue = b.getValue
        //     let sum = a.value |> Array.map (fun e -> e.value2D * (bValue e.i e.j)) |> Array.sum |> d.normalize
        //     sum
        //
        // member d.integrateValues (a : Matrix<double>) =
        //     let sum = a.value |> Array.map (fun e -> e |> Array.sum) |> Array.sum |> d.normalize
        //     sum
        //
        // member d.integrateValues (a : LinearMatrix<double>) =
        //     let sum = a.d1Range |> Array.map (fun i -> a.d2Range |> Array.map (fun j -> a.getValue i j) |> Array.sum) |> Array.sum |> d.normalize
        //     sum

        member d.integrateValues (a : SparseArray4D<double>, b : LinearMatrix<double>) : Matrix<double> =
            // a.value |> Array.map (fun v -> v |> Array.map (fun e -> d.integrateValues (e, b))) |> Matrix
            failwith ""

        member d.integrateValues (a : SparseArray4D<double>, b : LinearMatrix<double>) =
            match a with
            | StaticSparseArr4D s ->
                let x = s.value |> Array.map (fun v -> v |> Array.map (fun e -> d.integrateValues (e, b)))
                failwith ""
            | DynamicSparseArr4D d ->
                failwith ""
            // a.value |> Array.map (fun v -> v |> Array.map (fun e -> d.integrateValues (e, b))) |> Matrix

        // /// Calculates how many protocells are created.
        // member d.evolve (useParallel: bool, p : PoissonSampler, multiplier : double, a : SparseArray4D<double>, b : Matrix<int64>) =
        //     let evolveFunc i v = v |> Array.map (fun e -> d.evolve (p.getSampler i, multiplier, e, b))
        //
        //     if useParallel then
        //         let result = Array.zeroCreate a.value.Length
        //         let parallelOptions = ParallelOptions()
        //         parallelOptions.MaxDegreeOfParallelism <- 18 // Set the number of cores to use
        //         Parallel.For(0, a.value.Length, parallelOptions, fun i -> result.[i] <- evolveFunc i a.value[i]) |> ignore
        //         result |> Matrix
        //     else
        //         a.value |> Array.mapi evolveFunc |> Matrix

        /// Performs a Poisson "evolution" for a given state matrix.
        /// This is essentially a tau-leaping algorithm.
        /// The multiplier controls the step of the evolution.
        /// The values are considered non-negative and so all negative values are treated as zero.
        member d.evolve (useParallel: bool, p : PoissonSampler, multiplier : double, a : SparseArray4D<double>, b : Matrix<int64>) =
            match a with
            | StaticSparseArr4D s ->
                let mapi = if useParallel then Array.Parallel.mapi else Array.mapi
                s.value |> mapi (fun i v -> v |> Array.map (fun e -> d.evolve (p.getSampler i, multiplier, e, b))) |> Matrix
            | DynamicSparseArr4D d ->
                failwith ""

        member d.evolve (useParallel: bool, p : PoissonSampler, multiplier : double, a : SparseArray4D<double>, b : SparseArray2D<int64>) =
            match a with
            | StaticSparseArr4D s ->
                // let mapi = if useParallel then Array.Parallel.mapi else Array.mapi
                // s.value |> mapi (fun i v -> v |> Array.map (fun e -> d.evolve (p.getSampler i, multiplier, e, b))) |> Matrix
                failwith ""
            | DynamicSparseArr4D d ->
                failwith ""

        // /// Calculates how many protocells are created.
        // member d.evolve (p : PoissonSampler, a : SparseArray4D<double>, b : LinearMatrix<int64>) =
        //     a.value |> Array.map (fun v -> v |> Array.map (fun e -> d.evolve (p, e, b))) |> Matrix

        /// Calculates an integral matrix value for a given 4D sparse array.
        member d.integrateValues (a : SparseArray4D<double>) : SparseArray2D<double> =
            match a with
            | StaticSparseArr4D s ->
                let x = s.value |> Array.map (fun v -> v |> Array.map d.integrateValues) // |> Matrix

                failwith ""
            | DynamicSparseArr4D d ->
                failwith "todo"
            // a.value |> Array.map (fun v -> v |> Array.map d.integrateValues) |> Matrix

        member d.norm (a : LinearMatrix<double>) = d.integrateValues a

        member d.mean (a : LinearMatrix<double>) =
            let norm = d.norm a

            if norm > 0.0
            then
                let mx = (d.integrateValues (d.xDomain.points * a)) / norm
                let my = (d.integrateValues (a * d.yDomain.points)) / norm
                (mx, my)
            else (0.0, 0.0)

        member d.mean (a : Matrix<int64>) =
            let norm = a.total()
            let b = a.convert double

            if norm > 0L
            then
                let mx = (d.xDomain.points * b).total() / (double norm)
                let my = (b * d.yDomain.points).total() / (double norm)
                (mx, my)
            else (0.0, 0.0)

        member d.stdDev (a : LinearMatrix<double>) =
            let norm = d.norm a

            if norm > 0.0
            then
                let mx, my = d.mean a
                let m2x = (d.integrateValues (d.xDomain.points * (d.xDomain.points * a))) / norm
                let m2y = (d.integrateValues ((a * d.yDomain.points) * d.yDomain.points)) / norm
                (Math.Max(m2x - mx * mx, 0.0) |> Math.Sqrt, Math.Max(m2y - my * my, 0.0) |> Math.Sqrt)
            else (0.0, 0.0)

        member d.stdDev (a : Matrix<int64>) =
            let norm = a.total()
            let b = a.convert double

            if norm > 0L
            then
                let mx, my = d.mean a
                let m2x = (d.xDomain.points * (d.xDomain.points * b)).total() / (double norm)
                let m2y = ((b * d.yDomain.points) * d.yDomain.points).total() / (double norm)
                (Math.Max(m2x - mx * mx, 0.0) |> Math.Sqrt, Math.Max(m2y - my * my, 0.0) |> Math.Sqrt)
            else (0.0, 0.0)

        // static member eeMinValue = -1.0
        // static member eeMaxValue = 1.0
        // static member infDefaultMinValue = 0.0
        // static member defaultRanges = [| Domain2D.eeMinValue; Domain2D.eeMaxValue; Domain2D.infDefaultMinValue; InfMaxValue.defaultValue.value |]
        member d.ranges = [| d.xDomain.points.value[0]; Array.last d.xDomain.points.value; d.yDomain.points.value[0]; Array.last d.yDomain.points.value |]

        static member create noOfIntervals rX rY =
            let xDomain = Domain.create noOfIntervals rX
            let yDomain = Domain.create noOfIntervals rY

            {
                xDomain = xDomain
                yDomain = yDomain
            }

        // static member defaultValue = Domain2D.create 100 InfMaxValue.defaultValue.value
        //
        // member d.modelString =
        //     let a =
        //         if d.xDomain.noOfIntervals = d.yDomain.noOfIntervals
        //         then $"d{d.xDomain.noOfIntervals}"
        //         else $"d{d.xDomain.noOfIntervals}x{d.yDomain.noOfIntervals}"
        //
        //     match toModelStringArray Domain2D.defaultRanges d.ranges with
        //     | Some b -> $"{a}_{b}"
        //     | None -> a


    /// TODO kk:20231017 - Only scalar eps is supported for now.
    /// Type to describe a function used to calculate eps in mutation probability calculations.
    type EpsFunc =
        | EpsFunc of (Domain -> double -> double)

        member r.invoke = let (EpsFunc v) = r in v


    /// k0 multiplier in kA.
    type K0 =
        | K0 of double

        member r.value = let (K0 v) = r in v
        static member defaultIdentityValue = K0 1.0
        static member defaultValue = K0 0.1
        static member defaultSmallValue = K0 0.01
        static member defaultVerySmallValue = K0 0.001


    /// ka portion of the kernel.
    type KaFunc =
        | KaFunc of (Domain2D -> double -> double -> double)

        member r.invoke = let (KaFunc v) = r in v


    /// Decay function.
    type GammaFunc =
        | GammaFunc of (Domain2D -> double -> double -> double)

        member r.invoke = let (GammaFunc v) = r in v


    type Eps0 =
        | Eps0 of double

        member r.value = let (Eps0 v) = r in v
        static member defaultValue = Eps0 0.01
        static member defaultNarrowValue = Eps0 0.005
        static member defaultWideValue = Eps0 0.02
        // member e.modelString = toModelString Eps0.defaultValue.value e.value |> bindPrefix "e"


    type EpsFuncValue =
        | ScalarEps of Eps0

        member ef.epsFunc (_ : Domain) : EpsFunc =
            match ef with
            | ScalarEps e -> EpsFunc (fun _ _ -> e.value)

        // member ef.modelString =
        //     match ef with
        //     | ScalarEps e -> e.modelString |> Option.defaultValue EmptyString

    let separableFunc (v : SeparateTaylorApproximation2D) a b =
        let xVal = v.xTaylorApproximation.calculate a
        let yVal = v.yTaylorApproximation.calculate b
        xVal * yVal


    /// We want (2 / 3) of the domain range to scale to 1.0.
    let twoThirdInfScale (d : Domain2D) =
        let one = (2.0 / 3.0) * d.yDomain.domainRange.range
        let scale = 1.0 / one
        scale


    type KaFuncValue =
        | IdentityKa of double
        | SeparableKa of ScaledSeparateTaylorApproximation2D

        member k.kaFunc (_ : Domain2D) : KaFunc =
            match k with
            | IdentityKa v -> (fun _ _ _ -> v)
            | SeparableKa v -> (fun _ a b -> v.scale * (separableFunc v.separateTaylorApproximation2D a b))
            |> KaFunc

        /// Same as kaFunc but without k0.
        member k.kaFuncUnscaled (_ : Domain2D) : KaFunc =
            match k with
            | IdentityKa _ -> (fun _ _ _ -> 1.0)
            | SeparableKa v -> (fun _ a b -> separableFunc v.separateTaylorApproximation2D a b)
            |> KaFunc

        /// k0 - effective y scale of ka.
        member k.k0 =
            match k with
            | IdentityKa v -> v
            | SeparableKa v -> v.scale
            |> K0

        /// Changes k0 value.
        member k.withK0 (k0 : K0) =
            match k with
            | IdentityKa _ -> IdentityKa k0.value
            | SeparableKa v -> SeparableKa { v with scale = k0.value }

        member k.comparisonFactors =
            match k with
            | IdentityKa _ -> [||]
            | SeparableKa v -> v.comparisonFactors

        /// Default value is 1.0 on all domain.
        static member defaultValue = IdentityKa 1.0

        static member defaultQuadraticCoefficients = [| 1.0; 0.0; 1.0 |]

        /// Quadratic growth from (0, 0) point.
        static member defaultQuadraticValue (d : Domain2D) =
            let tEe =
                {
                    x0 = 0.0
                    xScale = 1.0
                    coefficients = KaFuncValue.defaultQuadraticCoefficients
                }

            let tInf =
                {
                    x0 = 0.0
                    xScale = twoThirdInfScale d
                    coefficients = KaFuncValue.defaultQuadraticCoefficients
                }

            SeparableKa { scale = K0.defaultValue.value; separateTaylorApproximation2D = { xTaylorApproximation = tEe; yTaylorApproximation = tInf } }

        /// Quadratic growth from (0, 0) point with a 0.01 linear growth in inf space.
        static member defaultQuadraticWithLinearInfValue (d : Domain2D) =
            let tEe =
                {
                    x0 = 0.0
                    xScale = 1.0
                    coefficients = [| 1.0; 0.0; 1.0 |]
                }

            let tInf =
                {
                    x0 = 0.0
                    xScale = twoThirdInfScale d
                    coefficients = [| 1.0; 0.01; 1.0 |]
                }

            SeparableKa { scale = K0.defaultValue.value; separateTaylorApproximation2D = { xTaylorApproximation = tEe; yTaylorApproximation = tInf } }

        /// Quadratic growth from (0, 0) point with a 0.1 linear growth in inf space.
        static member defaultQuadraticWithLinearInfValueI1 (d : Domain2D) =
            let tEe =
                {
                    x0 = 0.0
                    xScale = 1.0
                    coefficients = [| 1.0; 0.0; 1.0 |]
                }

            let tInf =
                {
                    x0 = 0.0
                    xScale = twoThirdInfScale d
                    coefficients = [| 1.0; 0.1; 1.0 |]
                }

            SeparableKa { scale = K0.defaultValue.value; separateTaylorApproximation2D = { xTaylorApproximation = tEe; yTaylorApproximation = tInf } }

        /// Quadratic growth from (0, 0) point with a 1.0 linear growth in inf space.
        static member defaultQuadraticWithLinearInfValueI10 (d : Domain2D) =
            let tEe =
                {
                    x0 = 0.0
                    xScale = 1.0
                    coefficients = [| 1.0; 0.0; 1.0 |]
                }

            let tInf =
                {
                    x0 = 0.0
                    xScale = twoThirdInfScale d
                    coefficients = [| 1.0; 1.0; 1.0 |]
                }

            SeparableKa { scale = K0.defaultValue.value; separateTaylorApproximation2D = { xTaylorApproximation = tEe; yTaylorApproximation = tInf } }

        // /// defaultQuadraticValue for a default Domain2D is the comparison value here.
        // member k.modelString =
        //     match k with
        //     | IdentityKa v -> $"kI{(toDoubleString v)}"
        //     | SeparableKa v ->
        //         let a = toModelString K0.defaultValue.value v.eeInfScale |> bindPrefix "k"
        //         let b = toModelStringArray KaFuncValue.defaultQuadraticCoefficients v.tEeInf.tEe.coefficients |> bindPrefix "_"
        //         let c = toModelStringArray KaFuncValue.defaultQuadraticCoefficients v.tEeInf.tInf.coefficients |> bindPrefix "i"
        //         let d = toModelStringArray (KaFuncValue.defaultQuadraticValue Domain2D.defaultValue).comparisonFactors k.comparisonFactors |> bindPrefix "@"
        //         let e = [| a; b; c; d |] |> Array.choose id |> joinStrings EmptyString
        //         e


    /// gamma0 multiplier in gamma.
    type Gamma0 =
        | Gamma0 of double

        member r.value = let (Gamma0 v) = r in v
        static member defaultValue = Gamma0 0.01


    type GlobalAsymmetryFactor =
        | GlobalAsymmetryFactor of double

        member r.value = let (GlobalAsymmetryFactor v) = r in v
        static member defaultValue = GlobalAsymmetryFactor -0.01
        static member defaultSmallValueX5 = GlobalAsymmetryFactor -0.005
        static member defaultSmallValueX2 = GlobalAsymmetryFactor -0.002
        static member defaultSmallValue = GlobalAsymmetryFactor -0.001
        static member defaultVerySmallValue = GlobalAsymmetryFactor -0.0001


    type GammaFuncValue =
        | ScalarGamma of double
        | SeparableGamma of ScaledSeparateTaylorApproximation2D

        member g.gammaFunc (_ : Domain2D) : GammaFunc =
            match g with
            | ScalarGamma e -> (fun _ _ _ -> e)
            | SeparableGamma e -> (fun _ a b -> e.scale * (separableFunc e.separateTaylorApproximation2D a b))
            |> GammaFunc

        member g.gammaFuncUnscaled (_ : Domain2D) : GammaFunc =
            match g with
            | ScalarGamma _ -> (fun _ _ _ -> 1.0)
            | SeparableGamma e -> (fun _ a b -> separableFunc e.separateTaylorApproximation2D a b)
            |> GammaFunc

        member g.gamma0 =
            match g with
            | ScalarGamma e -> e
            | SeparableGamma e -> e.scale
            |> Gamma0

        member g.comparisonFactors =
            match g with
            | ScalarGamma _ -> [||]
            | SeparableGamma v -> v.comparisonFactors

        static member withGamma0 (gamma0 : Gamma0) (g : GammaFuncValue) =
            match g with
            | ScalarGamma _ -> ScalarGamma gamma0.value
            | SeparableGamma e -> SeparableGamma { e with scale = gamma0.value }

        static member defaultValue = ScalarGamma Gamma0.defaultValue.value
        static member defaultNonLinearEeCoefficients = [| 1.0; GlobalAsymmetryFactor.defaultValue.value |]
        static member defaultNonLinearInfCoefficients = [| 1.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0; 1000.0 |]

        static member defaultNonLinearValue (d : Domain2D) =
            let tEe =
                {
                    x0 = 0.0
                    xScale = 1.0
                    coefficients = GammaFuncValue.defaultNonLinearEeCoefficients
                }

            let tInf =
                {
                    x0 = 0.0
                    xScale = twoThirdInfScale d
                    coefficients = GammaFuncValue.defaultNonLinearInfCoefficients
                }

            SeparableGamma { scale = Gamma0.defaultValue.value; separateTaylorApproximation2D = { xTaylorApproximation = tEe; yTaylorApproximation = tInf } }

        static member withGlobalAsymmetryFactor (a : GlobalAsymmetryFactor) (g : GammaFuncValue) =
            match g with
            | ScalarGamma _ -> failwith "Cannot set global asymmetry factor for scalar gamma."
            | SeparableGamma e -> SeparableGamma { e with separateTaylorApproximation2D = { e.separateTaylorApproximation2D with xTaylorApproximation = { e.separateTaylorApproximation2D.xTaylorApproximation with coefficients = [| 1.0; a.value |] } } }

        // member g.modelString =
        //     match g with
        //     | ScalarGamma e -> $"gS{(toDoubleString e)}"
        //     | SeparableGamma e ->
        //         let a = toModelString Gamma0.defaultValue.value e.eeInfScale |> bindPrefix "g"
        //         let b = toModelStringArray GammaFuncValue.defaultNonLinearEeCoefficients e.tEeInf.tEe.coefficients |> bindPrefix "a"
        //         let c = toModelStringArray GammaFuncValue.defaultNonLinearInfCoefficients e.tEeInf.tInf.coefficients |> bindPrefix "_"
        //         let d = toModelStringArray (GammaFuncValue.defaultNonLinearValue Domain2D.defaultValue).comparisonFactors g.comparisonFactors |> bindPrefix "@"
        //         let e = [| a; b; c; d |] |> Array.choose id |> joinStrings EmptyString
        //         e.Replace("-", "") // We don't care about the sign as the only negative coefficient is asymmetry factor (due to historical reasons).


    type MutationProbabilityParams =
        {
            domainParams : DomainParams
            zeroThreshold : ZeroThreshold<double>
            epsFuncValue : EpsFuncValue
        }


    /// Creates a normalized mutation probability.
    /// The normalization is performed using integral estimate over the domain.
    type MutationProbability =
        | MutationProbability of SparseArray<double>

        member r.value = let (MutationProbability v) = r in v

        /// m is a real (unscaled) value but e is scaled to the half of the range.
        /// i is an index in the domain.
        static member create (e : EvolutionType) (data : MutationProbabilityParams) (i : int) =
            let domain = data.domainParams.domain()
            let ef = data.epsFuncValue.epsFunc domain

            match e with
            | DifferentialEvolution ->
                let range = domain.domainRange.range
                let mean = domain.points.value[i]
                let epsFunc x = (ef.invoke domain x) * range / 2.0
                let f x : double = exp (- pown ((x - mean) / (epsFunc x)) 2)
                let values = domain.points.value |> Array.map f |> SparseArray<double>.createAbove data.zeroThreshold
                let norm = domain.integrateValues values
                let p = values.value |> Array.map (fun v -> { v with value1D = v.value1D / norm }) |> SparseArray<double>.create
                p
            | DiscreteEvolution ->
                let epsFunc (i1 : int) = (ef.invoke domain domain.points.value[i1]) * ( double domain.points.value.Length) / 2.0
                let f (i1 : int) : double = exp (- pown ((double (i1 - i)) / (epsFunc i1)) 2)
                let values = domain.points.value |> Array.mapi (fun i1 _ -> f i1) |> SparseArray<double>.createAbove data.zeroThreshold
                let norm = values.total()
                let p = values.value |> Array.map (fun v -> { v with value1D = v.value1D / norm }) |> SparseArray<double>.create
                p
            |> MutationProbability


    type MutationProbabilityParams2D =
        {
            xMutationProbabilityParams : MutationProbabilityParams
            yMutationProbabilityParams : MutationProbabilityParams
            sparseArrayType : SparseArrayType
        }

        member d.domain2D() =
            {
                xDomain = d.xMutationProbabilityParams.domainParams.domain()
                yDomain = d.yMutationProbabilityParams.domainParams.domain()
            }


    type MutationProbability2D =
        | MutationProbability2D of SparseArray2D<double>

        member r.value = let (MutationProbability2D v) = r in v

        /// i and j are indices in the domain.
        static member create (e : EvolutionType) (data : MutationProbabilityParams2D) (i : int) (j : int) =
            let p1 = MutationProbability.create e data.xMutationProbabilityParams i
            let p2 = MutationProbability.create e data.yMutationProbabilityParams j

            let p =
                match data.sparseArrayType with
                | StaticSparseArrayType -> cartesianMultiply p1.value p2.value
                | DynamicSparseArrayType -> SparseArray2D<double>.create (p1.value.value, p2.value.value)
                |> MutationProbability2D
            p


    /// Constructs p(x, y, x1, y1) sparse array where each numerical integral by (x, y) for each of (x1, y1) should be 1.
    /// Integration by (x, y) is "inconvenient" as normally we'd need to integrate by (x1, y1) and so the structure is
    /// optimized for that.
    type MutationProbability4D =
        {
            /// For integration over (x, y)
            x1y1_xy : SparseArray4D<double>

            /// For "standard" integration over (x1, y1)
            xy_x1y1 : SparseArray4D<double>
        }

        static member create (e : EvolutionType) (data : MutationProbabilityParams2D) : MutationProbability4D =
            let domain2D = data.domain2D()
            let xMu = domain2D.xDomain.points.value
            let yMu = domain2D.yDomain.points.value

            let p =
                match data.sparseArrayType with
                | StaticSparseArrayType ->
                    // These are the values where integration by (x, y) should yield 1 for each (x1, y1).
                    // So [][] is by (x1, y1) and the underlying SparseArray2D is by (x, y).
                    let x1y1_xy = xMu |> Array.mapi (fun i _ -> yMu |> Array.mapi (fun j _ -> (MutationProbability2D.create e data i j).value))

                    let xy_x1y1_Map =
                        [| for i in 0..(xMu.Length - 1) -> [| for j in 0..(yMu.Length - 1) -> SparseValue4D.createSeq i j ((x1y1_xy[i][j])) |] |]
                        |> Array.concat
                        |> Seq.concat
                        |> Seq.toArray
                        |> Array.groupBy (fun e -> e.i1, e.j1)
                        |> Array.map (fun (a, b) -> a, b |> Array.map (fun e -> { i = e.i; j = e.j; value2D = e.value4D }) |> Array.sortBy (fun e -> e.i, e.j) |> SparseArray2D<double>.create)
                        |> Map.ofArray

                    let xy_x1y1 =
                        xMu
                        |> Array.mapi (fun i _ -> yMu |> Array.mapi (fun j _ -> xy_x1y1_Map[(i, j)]))

                    {
                        x1y1_xy = x1y1_xy |> StaticSparseArray4D |> StaticSparseArr4D
                        xy_x1y1 = xy_x1y1 |> StaticSparseArray4D |> StaticSparseArr4D
                    }
                | DynamicSparseArrayType ->
                    let xMp = xMu |> Array.mapi (fun i _ -> (MutationProbability.create e data.xMutationProbabilityParams i).value)
                    let yMp = xMu |> Array.mapi (fun j _ -> (MutationProbability.create e data.yMutationProbabilityParams j).value)

                    let p1 = xMu |> Array.mapi (fun i _ -> xMp[i].value)
                    let p2 = yMu |> Array.mapi (fun j _ -> yMp[j].value)

                    let x1y1_xy = fun i1 j1 -> SparseArray2D<double>.create (p1[i1], p2[j1])

                    let x_x1_Map =
                        [| for i in 0..(xMu.Length - 1) -> SparseValue2D.createArray i (xMp[i]) |]
                        |> Array.concat
                        |> Array.groupBy (fun e -> e.j)
                        |> Array.map (fun (a, b) -> a, b |> Array.map (fun e -> { i = e.i; value1D = e.value2D }) |> Array.sortBy (fun e -> e.i) |> SparseArray<double>.create)
                        |> Map.ofArray

                    let x_x1 = xMu |> Array.mapi (fun i _ -> x_x1_Map[i])

                    let y_y1_Map =
                        [| for i in 0..(yMu.Length - 1) -> SparseValue2D.createArray i (yMp[i]) |]
                        |> Array.concat
                        |> Array.groupBy (fun e -> e.j)
                        |> Array.map (fun (a, b) -> a, b |> Array.map (fun e -> { i = e.i; value1D = e.value2D }) |> Array.sortBy (fun e -> e.i) |> SparseArray<double>.create)
                        |> Map.ofArray

                    let y_y1 = yMu |> Array.mapi (fun i _ -> y_y1_Map[i])

                    let xy_x1y1 = fun i j -> SparseArray2D<double>.create (x_x1[i].value, y_y1[j].value)

                    {
                        x1y1_xy =
                            {
                                getData2D = x1y1_xy
                                xLength = xMp.Length
                                yLength = yMp.Length
                            }
                            |> DynamicSparseArray4D |> DynamicSparseArr4D
                        xy_x1y1 =
                            {
                                getData2D = xy_x1y1
                                xLength = xMp.Length
                                yLength = yMp.Length
                            }
                            |> DynamicSparseArray4D |> DynamicSparseArr4D
                    }

            p


    type KernelParams =
        {
            domainIntervals : DomainIntervals
            xRange : DomainRange
            yRange : DomainRange
            zeroThreshold : ZeroThreshold<double>
            xEpsFuncValue : EpsFuncValue
            yEpsFuncValue : EpsFuncValue
            kaFuncValue : KaFuncValue
        }

        member kp.xDomainParams =
            {
                domainIntervals = kp.domainIntervals
                domainRange = kp.xRange
            }

        member kp.yDomainParams =
            {
                domainIntervals = kp.domainIntervals
                domainRange = kp.yRange
            }

        member kp.mutationProbabilityData2D =
            {
                xMutationProbabilityParams =
                    {
                        domainParams = kp.xDomainParams
                        zeroThreshold = ZeroThreshold.defaultValue
                        epsFuncValue = kp.xEpsFuncValue
                    }
                yMutationProbabilityParams =
                    {
                        domainParams = kp.yDomainParams
                        zeroThreshold = ZeroThreshold.defaultValue
                        epsFuncValue = kp.yEpsFuncValue
                    }
                sparseArrayType = failwith ""
            }

        member kp.domain2D() = Domain2D.create kp.domainIntervals kp.xRange kp.yRange

    //     static member defaultValue =
    //         {
    //             domainIntervals = DomainIntervals.defaultValue
    //             infMaxValue = InfMaxValue.defaultValue
    //             zeroThreshold = ZeroThreshold.defaultValue
    //             epsEeFuncValue = EpsFuncValue.ScalarEps Eps0.defaultValue
    //             epsInfFuncValue = EpsFuncValue.ScalarEps Eps0.defaultValue
    //             kaFuncValue = KaFuncValue.IdentityKa 1.0
    //         }
    //
    //     /// Same as above but with quadratic ka.
    //     static member defaultQuadraticValue =
    //         let kp = KernelParams.defaultValue
    //         { kp with kaFuncValue = KaFuncValue.defaultQuadraticValue (kp.domain2D()) }
    //
    //     static member defaultQuadraticWithLinearInfValue =
    //         let kp = KernelParams.defaultValue
    //         { kp with kaFuncValue = KaFuncValue.defaultQuadraticWithLinearInfValue (kp.domain2D()) }
    //
    //     static member withEps0 (eps0 : Eps0) (kp : KernelParams) =
    //         { kp with epsEeFuncValue = EpsFuncValue.ScalarEps eps0; epsInfFuncValue = EpsFuncValue.ScalarEps eps0 }
    //
    //     static member withK0 (k0 : K0) (kp : KernelParams) = { kp with kaFuncValue = kp.kaFuncValue.withK0 k0 }
    //     static member withKaFunc (ka : KaFuncValue) (kp : KernelParams) = { kp with kaFuncValue = ka }
    //     static member withDomainIntervals (d : DomainIntervals) (kp : KernelParams) = { kp with domainIntervals = d }


    type Ka =
        | Ka of Matrix<double>

        member r.value = let (Ka v) = r in v


    type Kernel =
        | Kernel of SparseArray4D<double>

        member r.value = let (Kernel v) = r in v


    /// Represents K(x, x1, y, y1) 2D Fredholm kernel.
    /// It is convenient to store it in a form,
    /// where the first two indexes are (x, y) and last ones are (x1, y1).
    /// So the actual indexes here are K(x, y, x1, y1).
    type KernelData =
        {
            kernel : Kernel
            ka : Ka
            domain2D : Domain2D
        }

        member k.integrateValues (u : LinearMatrix<double>) : Matrix<double> =
            let v = k.domain2D.integrateValues (k.kernel.value, u)
            v

        /// Calculates how many protocells are created.
        /// The multiplier carries the value in front of the integral (f^n).
        member k.evolve (useParallel : bool) (p : PoissonSampler) (multiplier : double) (u : Matrix<int64>) =
            let v = k.domain2D.evolve (useParallel, p, multiplier, k.kernel.value, u)
            v

        static member create (e : EvolutionType) (p : KernelParams) =
            let domain2D = Domain2D.create p.domainIntervals p.xRange p.yRange

            let mp2 =
                {
                    xMutationProbabilityParams =
                        {
                            domainParams = p.xDomainParams
                            zeroThreshold = p.zeroThreshold
                            epsFuncValue = p.xEpsFuncValue
                        }

                    yMutationProbabilityParams =
                        {
                            domainParams = p.yDomainParams
                            zeroThreshold = p.zeroThreshold
                            epsFuncValue = p.yEpsFuncValue
                        }
                    sparseArrayType = failwith ""
                }

            let mp4 = MutationProbability4D.create e mp2
            let kaFunc = p.kaFuncValue.kaFunc domain2D

            let ka =
                domain2D.xDomain.points.value
                |> Array.map (fun x -> domain2D.yDomain.points.value |> Array.map (fun y -> kaFunc.invoke domain2D x y))
                |> Matrix

            let kernel = mp4.xy_x1y1 * ka |> Kernel

            {
                kernel = kernel
                ka = Ka ka
                domain2D = domain2D
            }

    type Gamma =
        | Gamma of Matrix<double>

        member r.value = let (Gamma v) = r in v

        /// Calculates how many protocells are destroyed.
        member r.evolve (p : PoissonSingleSampler) (u : Matrix<int64>) =
            let m = r.value.value

            let g i j v =
                if v <= 0L then 0L
                else
                    let lambda = (double v) * m[i][j]
                    let retVal = p.nextPoisson lambda
                    min retVal v // Cannot destroy more than we have.

            let retVal = u.value |> Array.mapi (fun i a -> a |> Array.mapi (fun j b -> g i j b)) |> Matrix
            retVal

        static member create (d : Domain2D) (g : GammaFuncValue) : Gamma =
            let gamma =
                d.xDomain.points.value
                |> Array.map (fun a -> d.yDomain.points.value |> Array.map (fun b -> (g.gammaFunc d).invoke d a b))
                |> Matrix
                |> Gamma

            gamma


    /// Number of "molecules" or building blocks used in a protocell.
    /// This controls the non-linearity of the creation model.
    /// Default value is set to 1 because we take into account that a single protocell encounters with food
    /// proportionally to concentration of the food.
    type NumberOfMolecules =
        | NumberOfMolecules of int

        member r.value = let (NumberOfMolecules v) = r in v
        static member defaultValue = NumberOfMolecules 1
        static member defaultValue2 = NumberOfMolecules 2


    type RecyclingRate =
        | RecyclingRate of double

        member r.value = let (RecyclingRate v) = r in v
        static member defaultValue = RecyclingRate 1.0

        // member w.modelString =
        //     toModelString RecyclingRate.defaultValue.value w.value
        //     |> bindPrefix "w"
        //     |> Option.defaultValue EmptyString

        /// Calculates how much waste is recycled.
        member r.evolve (p : PoissonSingleSampler) w =
            if w <= 0L then 0L
            else
                let lambda = r.value * (double w)
                let retVal = p.nextPoisson lambda
                min retVal w // Cannot recycle more than we have.


    // /// Common parameters between differential and Poisson based models.
    // type EeInfModelParams =
    //     {
    //         kernelParams : KernelParams
    //         gammaFuncValue : GammaFuncValue
    //         numberOfMolecules : NumberOfMolecules
    //         recyclingRate : RecyclingRate
    //         name : string
    //     }
    //
    //     member mp.modelString =
    //         let domain = mp.kernelParams.domain2D()
    //
    //         [|
    //             domain.modelString
    //             mp.kernelParams.kaFuncValue.modelString
    //             mp.kernelParams.epsEeFuncValue.modelString
    //             mp.gammaFuncValue.modelString
    //             mp.recyclingRate.modelString
    //         |]
    //         |> joinStrings EmptyString
    //
    //     /// Default linear value, mostly for tests, as it does not have many practical purposes.
    //     static member defaultValue =
    //         {
    //             kernelParams = KernelParams.defaultValue
    //             gammaFuncValue = GammaFuncValue.defaultValue
    //             numberOfMolecules = NumberOfMolecules.defaultValue
    //             recyclingRate = RecyclingRate.defaultValue
    //             name = EmptyString
    //         }
    //
    //     /// Default value with quadratic kernel and non-linear gamma.
    //     /// This is the main starting point where we can vary k0, eps0, gamma0, etc...
    //     static member defaultNonLinearValue =
    //         let kp = KernelParams.defaultQuadraticValue
    //         let d = kp.domain2D()
    //         { EeInfModelParams.defaultValue with kernelParams = kp; gammaFuncValue = GammaFuncValue.defaultNonLinearValue d }
    //
    //     static member defaultQuadraticWithLinearInfValue =
    //         let kp = KernelParams.defaultQuadraticWithLinearInfValue
    //         let d = kp.domain2D()
    //         { EeInfModelParams.defaultValue with kernelParams = kp; gammaFuncValue = GammaFuncValue.defaultNonLinearValue d }
    //
    //     static member withK0 k0 p = { p with kernelParams = p.kernelParams |> KernelParams.withK0 k0 }
    //     static member withKaFunc (ka : KaFuncValue) p = { p with kernelParams = p.kernelParams |> KernelParams.withKaFunc ka }
    //     static member withEps0 eps0 p = { p with kernelParams = p.kernelParams |> KernelParams.withEps0 eps0 }
    //     static member withGamma0 gamma0 p = { p with gammaFuncValue = p.gammaFuncValue |> GammaFuncValue.withGamma0 gamma0 }
    //     static member withDomainIntervals d p = { p with kernelParams = p.kernelParams |> KernelParams.withDomainIntervals d }
    //     static member withInfMaxValue infMaxValue p = { p with kernelParams = { p.kernelParams with infMaxValue = infMaxValue } }
    //     static member withGlobalAsymmetryFactor a p = { p with gammaFuncValue = p.gammaFuncValue |> GammaFuncValue.withGlobalAsymmetryFactor a }
