namespace Softellect.Math

open Softellect.Math.Primitives
open Softellect.Math.Sparse
open Softellect.Math.Evolution

/// TODO kk:20250315 - This is not generic enough to be sitting here.
///     However, I need to run some tests to see how all the new logic works.
module Models =

    type NoOfEpochs =
        | NoOfEpochs of int

        member r.value = let (NoOfEpochs v) = r in v


    /// Number of "molecules" or building blocks used in a protocell.
    /// This controls the non-linearity of the creation model.
    /// Default value is set to 1 because we take into account that a single protocell encounters with food
    /// proportionally to concentration of the food.
    type NumberOfMolecules =
        | NumberOfMolecules of int

        member r.value = let (NumberOfMolecules v) = r in v
        static member defaultValue = NumberOfMolecules 1


    type RecyclingRate =
        | RecyclingRate of double

        member r.value = let (RecyclingRate v) = r in v
        static member defaultValue = RecyclingRate 1.0

        // member w.modelString =
        //     toModelString RecyclingRate.defaultValue.value w.value
        //     |> bindPrefix "w"
        //     |> Option.defaultValue EmptyString

        /// Calculates how much waste is recycled.
        member r.evolveStep (p : PoissonSampler<int64>) w =
            if w <= 0L then 0L
            else
                let lambda = r.value * (double w)
                let retVal = p.next lambda
                min retVal w // Cannot recycle more than we have.


    type FoodData =
        | FoodData of int64

        member r.value = let (FoodData v) = r in v


    type WasteData =
        | WasteData of int64

        member r.value = let (WasteData v) = r in v


    type ProtoCellData<'I when ^I: equality and ^I: comparison> =
        | ProtoCellData of SparseArray<'I, int64>

        member r.value = let (ProtoCellData v) = r in v
        member r.total() = r.value.total()


    type SubstanceData<'I when 'I: equality and 'I: comparison> =
        {
            food : FoodData
            waste : WasteData
            protocell : ProtoCellData<'I>
        }


    type MoleculeCount =
        | MoleculeCount of int64

        member r.value = let (MoleculeCount v) = r in v
        static member OneThousand = MoleculeCount 1_000L // 10^3 - K
        static member OneMillion = MoleculeCount 1_000_000L // 10^6 - M
        static member OneBillion = MoleculeCount 1_000_000_000L // 10^9 - G
        static member OneTrillion = MoleculeCount 1_000_000_000_000L // 10^12 - T
        static member OneQuadrillion = MoleculeCount 1_000_000_000_000_000L // 10^15 - P
        static member OneQuintillion = MoleculeCount 1_000_000_000_000_000_000L // 10^18 - E


    type ModelInitParams =
        {
            uInitial : MoleculeCount
            totalMolecules : MoleculeCount
            seedValue : int
        }

        static member defaultValue =
            {
                uInitial = MoleculeCount.OneThousand
                totalMolecules = MoleculeCount.OneBillion
                seedValue = 1
            }


    type ModelContext<'I, 'T, 'S when 'I: equality and 'I: comparison and 'T: equality and 'T: comparison> =
        {
            evolutionContext : EvolutionContext<'I, 'T>
            noOfEpochs : NoOfEpochs
            initialData : 'S
            callBack : int -> 'S -> unit
        }


    /// A very simple arbitrary dimension evolutionary model (x is a point in the domain):
    ///     du(x, t) / dt = f(t)^n * k0 * sum(ka(y) * p(x,y) * u(y, t), {y}) - g0 * g(x) * u(x, t)
    ///     df(t) / dt = n * (-f(t)^n * k0 * sum(sum(ka(y) * p(x,y) * u(y,t), {y}), {x}) + s * w(t))
    ///     dw(t) / dt = -s * w(t) + g0 * sum(g(x) * u(x, t), {x})
    ///
    /// where:
    ///     f(t) * k0 * ka(y) * p(x, y) is an EvolutionMatrix - replication rate of evolving from y to x.
    ///     g0 * g(x) is a Multiplier - decay rate.
    ///     s is a constant recycling rate.
    type SimpleEvolutionModel<'I, 'C when 'I: equality and 'I: comparison> =
        {
            replication : EvolutionMatrix<'I>
            decay : Multiplier<'I>
            recyclingRate : RecyclingRate
            numberOfMolecules : NumberOfMolecules
            converter : ConversionParameters<'I, 'C>
        }

        member md.mean (x : SubstanceData<'I>) : 'C =
            x.protocell.value.mean md.converter

        member md.stdDev (x : SubstanceData<'I>) : 'C =
            (x.protocell.value.variance md.converter) |> md.converter.arithmetic.sqrt

        member md.invariant (x : SubstanceData<'I>) =
            let f = x.food.value
            let w = x.waste.value
            let u = x.protocell.value
            let n = md.numberOfMolecules.value

            let int_u = u.total()
            let inv = (int64 n) * (int_u + w) + f
            inv

        member md.evolveStep (p : EvolutionContext<'I, int64>) (x : SubstanceData<'I>) : SubstanceData<'I> =
            let f = x.food.value
            let w = x.waste.value
            let u = x.protocell.value
            let s = p.poissonSampler

            let n = md.numberOfMolecules.value

            let r = md.recyclingRate.evolveStep s w
            let gamma_u = md.decay.evolveStep p u
            let int_gamma_u = gamma_u.total()
            let f_n = (pown (double (max f 0L)) n)
            let int_k_u = md.replication.evolveStep p f_n u
            let int_int_k_u = int_k_u.total()

            // Note that the food could be "eaten" beyond zero. If that happens, then it will be treated as exact zero until enough waste is recycled.
            let df = (int64 n) * (r - int_int_k_u)
            let dw = - r + int_gamma_u
            let du = int_k_u.subtract gamma_u

            if f + df >= 0L then
                let f1 = f + df |> FoodData
                let w1 = w + dw |> WasteData
                let u1 = u.add du |> ProtoCellData

                let retVal =  { food = f1; waste = w1; protocell = u1 }
                retVal
            else
                // We get here if the food is getting eaten too fast.
                // Here is what we do:
                //   1. Adjust f_n.
                let c = (double int_int_k_u) / f_n
                let f_n1 = max (min (((double f) + (double r) * (double n)) / (c * (double n))) f_n) 0.0

                //   2. Recalculate df and du.
                let int_k_u = md.replication.evolveStep p f_n1 u
                let int_int_k_u = int_k_u.total()

                let df = (int64 n) * (r - int_int_k_u)
                let du = int_k_u.subtract gamma_u

                let f1 = f + df |> FoodData
                let w1 = w + dw |> WasteData
                let u1 = u.add du |> ProtoCellData

                let retVal =  { food = f1; waste = w1; protocell = u1 }
                retVal

        member md.evolve (ctx : ModelContext<'I, int64, SubstanceData<'I>>)=
             let g acc i =
                 let r = md.evolveStep ctx.evolutionContext acc
                 ctx.callBack i r
                 r

             let result = [| for i in 0..ctx.noOfEpochs.value -> i |] |> Array.fold g ctx.initialData
             result

    type SimpleEvolutionModel1D = SimpleEvolutionModel<Point1D, Coord1D>
    type SimpleEvolutionModel2D = SimpleEvolutionModel<Point2D, Coord2D>
    type SimpleEvolutionModel3D = SimpleEvolutionModel<Point3D, Coord3D>
    type SimpleEvolutionModel4D = SimpleEvolutionModel<Point4D, Coord4D>
    type SimpleEvolutionModel5D = SimpleEvolutionModel<Point5D, Coord5D>
    type SimpleEvolutionModel6D = SimpleEvolutionModel<Point6D, Coord6D>
    type SimpleEvolutionModel7D = SimpleEvolutionModel<Point7D, Coord7D>
    type SimpleEvolutionModel8D = SimpleEvolutionModel<Point8D, Coord8D>
