namespace Softellect.DistributedProcessing.SolverRunner

open System
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.SolverRunner.Primitives
open Softellect.DistributedProcessing.Errors
open System
open Microsoft.FSharp.NativeInterop
//open Primitives.GeneralPrimitives
//open Primitives.SolverPrimitives
//open Primitives.SolverRunnerErrors
open Softellect.OdePackInterop
open Microsoft.FSharp.Core
//open Primitives.GeneralData
//open GenericOdeSolver.Primitives
open Softellect.Sys.Primitives

#nowarn "9"

module OdeSolver =

    let private makeNonNegativeByRef (eps : double) (neq : int) (x : nativeptr<double>) : double[] =
        let g v = if v < eps then 0.0 else v
        [| for i in 0..(neq - 1) -> g (NativePtr.get x i) |]


    let private toArray (neq : int) (x : nativeptr<double>) : double[] = [| for i in 0..(neq - 1) -> NativePtr.get x i |]


    let private fUseNonNegative (
                                odeParams : OdeParams,
                                tryCallBack : TryCallBack<double[]>,
                                neq : byref<int>,
                                t : byref<double>,
                                x : nativeptr<double>,
                                dx : nativeptr<double>) : unit =

        let x1 = makeNonNegativeByRef odeParams.odeSolverType.correction neq x
        let et = decimal t |> EvolutionTime
        tryCallBack.invoke et x1

        match odeParams.derivative with
        | OneByOne f -> for i in 0..(neq - 1) do NativePtr.set dx i (f t x1 i)
        | FullArray f ->
            let d = f t x1
            for i in 0..(neq - 1) do NativePtr.set dx i d[i]


    let private fDoNotCorrect (
                                odeParams : OdeParams,
                                tryCallBack : TryCallBack<double[]>,
                                neq : byref<int>,
                                t : byref<double>,
                                x : nativeptr<double>,
                                dx : nativeptr<double>) : unit =

        // if needsCallBack.invoke t
        // then
        //     let x1 = toArray neq x
        //     callBack.invoke t x1
        //
        // let d = calculateDerivative t x
        // for i in 0 .. (neq - 1) do NativePtr.set dx i d[i]
        failwith "fDoNotCorrect is not implemented yet."


    let private createUseNonNegativeInterop n c = Interop.F(fun m t y dy -> fUseNonNegative(n, c, &m, &t, y, dy))
    let private createDoNotCorrectInterop n c = Interop.F(fun m t y dy -> fDoNotCorrect(n, c, &m, &t, y, dy))


    /// F# wrapper around various ODE solvers.
    let createOdeSolver p n =
        let startTime = double p.startTime.value
        let endTime = double p.endTime.value

        // (data : SolverData<'D, 'P, double[]>)
        // c
        match n.odeSolverType with
        | AlgLib CashCarp ->
            //n.logger.logDebugString "nSolve: Using Cash - Carp Alglib solver."

            let solve (_, x0) (c : TryCallBack<double[]>) =
                let nt = 2

                let cashCarpDerivative (x : double[]) (t : double) : double[] =
                    c.invoke (decimal t |> EvolutionTime) x
                    n.derivative.calculate t x

                let x : array<double> = [| for i in 0..nt -> startTime + (endTime - startTime) * (double i) / (double nt) |]
                let d = alglib.ndimensional_ode_rp (fun x t y _ -> cashCarpDerivative x t |> Array.mapi(fun i e -> y[i] <- e) |> ignore)
                let mutable s = alglib.odesolverrkck(x0, x, n.absoluteTolerance.value, n.stepSize)
                do alglib.odesolversolve(s, d, null)
                let mutable m, xTbl, yTbl, rep = alglib.odesolverresults(s)
                let xEnd = yTbl[nt - 1, *]
                //notifyAll n (FinalCallBack CompletedCalculation) { progressData = needsCallBackData.progressData; t = p.endTime; x = xEnd }

                (p.endTime, xEnd)

            SolverRunner solve

        | OdePack (m, i, nc) ->
            //n.logger.logDebugString $"nSolve: Using {m} / {i} / {nc} DLSODE solver."
            let mapResults (r : SolverResult) _ = (decimal r.EndTime |> EvolutionTime, r.X)

            let solve (_, x0) (c : TryCallBack<double[]>) =
                let result =
                    match nc with
                    | UseNonNegative _ ->
                        OdeSolver.RunFSharp(
                                (fun() -> createUseNonNegativeInterop n c),
                                m.value,
                                i.value,
                                startTime,
                                endTime,
                                x0,
                                mapResults,
                                n.absoluteTolerance.value)

                    | DoNotCorrect ->
                        OdeSolver.RunFSharp(
                                (fun() -> createDoNotCorrectInterop n c),
                                m.value,
                                i.value,
                                startTime,
                                endTime,
                                x0,
                                mapResults,
                                n.absoluteTolerance.value)

                //notifyAll n (FinalCallBack CompletedCalculation) result
                result

            SolverRunner solve
