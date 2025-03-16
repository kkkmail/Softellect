namespace Softellect.Math

open Softellect.Math.Primitives
open Softellect.Math.Sparse

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


    type SubstanceData<'I when ^I: equality and ^I: comparison> =
        {
            food : FoodData
            waste : WasteData
            protocell : ProtoCellData<'I>
        }

        // member inline d.evolve (p : EvolutionContext<'I, 'T>) =
        //
        //     failwith ""


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
            // protocellInitParams : EeInfDiffModel.ProtocellInitParams
            totalMolecules : MoleculeCount
            seedValue : int
        }

        static member defaultValue =
            {
                uInitial = MoleculeCount.OneThousand
                // protocellInitParams = EeInfDiffModel.ProtocellInitParams.defaultValue
                totalMolecules = MoleculeCount.OneBillion
                seedValue = 1
            }


    /// A very simple evolutionary model:
    ///     du(x, t) / dt = r(t) * k0 * sum(ka(y) * p(x,y) * u(y, t), {y}) - g0 * g(x) * u(x, t)
    ///     dr(t) / dt = -r(t) * k0 * sum(sum(ka(y) * p(x,y) * u(y,t), {y}), {x}) + s * w(t)
    ///     dw(t) / dt = -s * w(t) + g0 * sum(g(x) * u(x, t), {x})
    ///
    /// where:
    ///     k0 * ka(y) * p(x, y) is an EvolutionMatrix - replication rate of evolving from y to x.
    ///     g0 * g(x) is a Multiplier - decay rate.
    ///     s is a constant recycling rate.
    // type SimpleEvolutionModel<'I, 'C, 'D
    //     when 'I: equality
    //     and 'I: comparison
    //     and 'I: (member toCoord : 'D -> 'C)
    //     and 'C: equality
    //     and 'C: comparison> =
    //     {
    //         replication : EvolutionMatrix<'I>
    //         decay : Multiplier<'I>
    //         recyclingRate : RecyclingRate
    //         numberOfMolecules : NumberOfMolecules
    //         domain : 'D
    //     }
    //
    //     member inline md.mean (x : SubstanceData<'I>) : 'C =
    //         let converter (v : int64) : double = double v
    //         let projector (p : 'I) : 'C = p.toCoord md.domain
    //         let u = x.protocell.value
    //         let mean = u.mean converter projector
    //         mean

    type SimpleEvolutionModel<'I, 'C, 'D
            when ^I: equality
            and ^I: comparison
            and ^I: (member toCoord : ^D -> ^C)
            and ^C: equality
            and ^C: comparison
            and ^C: (static member ( + ) : ^C * ^C -> ^C)
            and ^C: (static member ( - ) : ^C * ^C -> ^C)
            // and ^C: (static member ( * ) : ^C * ^C -> ^C)
            // and ^C: (static member ( * ) : ^'C * double -> ^C)
            // and ^C: (static member ( * ) : double * ^C -> ^C)
            // and ^C: (static member ( / ) : ^C * double -> ^C)

            // and ^C: (static member op_Multiply : ^C * double -> ^C)
            // and ^C: (static member op_Division : ^C * double -> ^C)

            and ^C: (static member Zero : ^C)
            and ^C: (static member One : ^C)> =
        {
            replication : EvolutionMatrix<'I>
            decay : Multiplier<'I>
            recyclingRate : RecyclingRate
            numberOfMolecules : NumberOfMolecules
            domain : 'D
        }

        // member inline md.mean (x : SubstanceData<'I>) : 'C =
        //     x.protocell.value.mean double (fun (p : 'I) -> p.toCoord md.domain)

        //==============================================================

    // type SimpleEvolutionModel<'I, 'C, 'D
    //         when ^I: equality
    //         and ^I: comparison
    //         and ^I: (member toCoord : ^D -> ^C)
    //         and ^C: equality
    //         and ^C: comparison
    //         and ^C: (static member (+) : ^C * ^C -> ^C)
    //         and ^C: (static member (-) : ^C * ^C -> ^C)
    //         and ^C: (static member (*) : ^C * double -> ^C)
    //         // and ^C: (static member (*) : double * ^C -> ^C)
    //         and ^C: (static member (/) : ^C * double -> ^C)
    //         and ^C: (static member get_Zero : unit -> ^C)
    //         and ^C: (static member get_One : unit -> ^C)> =
    //     {
    //         replication : EvolutionMatrix<'I>
    //         decay : Multiplier<'I>
    //         recyclingRate : RecyclingRate
    //         numberOfMolecules : NumberOfMolecules
    //         domain : 'D
    //     }

        // member inline md.mean (x : SubstanceData<'I>) : ^C =
        //     let projector (p : 'I) : ^C = p.toCoord md.domain
        //     let converter (v : int64) = double v
        //     let u = x.protocell.value
        //     let mean = u.mean converter projector
        //     mean


        //==============================================================

        member inline md.invariant (x : SubstanceData<'I>) =
            let f = x.food.value
            let w = x.waste.value
            let u = x.protocell.value
            let n = md.numberOfMolecules.value

            let int_u = u.total()
            let inv = (int64 n) * (int_u + w) + f
            inv

        // member inline md.mean (x : SubstanceData<'I>) =
        //     let projector (p : 'I) = p.toCoord md.domain
        //     let converter (v : int64) = double v
        //     let u = x.protocell.value
        //     let mean = u.mean converter projector
        //     mean

        member inline md.evolve (p : EvolutionContext<'I, int64>, x : SubstanceData<'I>) =
            let f = x.food.value
            let w = x.waste.value
            let u = x.protocell.value
            let s = p.sampler

            let n = md.numberOfMolecules.value

            let r = md.recyclingRate.evolve s w
            let gamma_u = u.evolve (p, md.decay)
            let int_gamma_u = gamma_u.total()
            let f_n = (pown (double (max f 0L)) n)
            let int_k_u = u.evolve (p, md.replication, f_n)
            let int_int_k_u = int_k_u.total()

            // Note that the food could be "eaten" beyond zero. If that happens, then it will be treated as exact zero until enough waste is recycled.
            let df = (int64 n) * (r - int_int_k_u)
            let dw = - r + int_gamma_u
            let du = int_k_u - gamma_u

            if f + df >= 0L then
                let f1 = f + df |> FoodData
                let w1 = w + dw |> WasteData
                let u1 = u + du |> ProtoCellData

                let retVal =  { food = f1; waste = w1; protocell = u1 }
                retVal
            else
                // We get here if the food is getting eaten too fast.
                // Here is what we do:
                //   1. Adjust f_n.
                let c = (double int_int_k_u) / f_n
                let f_n1 = max (min (((double f) + (double r) * (double n)) / (c * (double n))) f_n) 0.0

                //   2. Recalculate df and du.
                let int_k_u = u.evolve (p, md.replication, f_n1)
                let int_int_k_u = int_k_u.total()

                let df = (int64 n) * (r - int_int_k_u)
                let du = int_k_u - gamma_u

                let f1 = f + df |> FoodData
                let w1 = w + dw |> WasteData
                let u1 = u + du |> ProtoCellData

                let retVal =  { food = f1; waste = w1; protocell = u1 }
                retVal

        member inline md.evolve (p : EvolutionContext<'I, int64>, x0 : SubstanceData<'I>, n : NoOfEpochs) =
             let result = [|for i in 0..n.value -> i |] |> Array.fold (fun acc _ -> md.evolve (p, acc)) x0
             result


    // let getMean (md: SimpleEvolutionModel<'I, 'C, 'D>) x =
    //     let converter (v : int64) : double = double v
    //     let projector (p : 'I) : 'C = p.toCoord md.domain
    //     let u = x.protocell.value
    //     let mean = u.mean converter projector
    //     mean

    type SimpleEvolutionModel1D = SimpleEvolutionModel<Point1D, Coord1D, Domain>
    type SimpleEvolutionModel2D = SimpleEvolutionModel<Point2D, Coord2D, Domain2D>
    type SimpleEvolutionModel3D = SimpleEvolutionModel<Point3D, Coord3D, Domain3D>
    type SimpleEvolutionModel4D = SimpleEvolutionModel<Point4D, Coord4D, Domain4D>
    type SimpleEvolutionModel5D = SimpleEvolutionModel<Point5D, Coord5D, Domain5D>
    type SimpleEvolutionModel6D = SimpleEvolutionModel<Point6D, Coord6D, Domain6D>
    type SimpleEvolutionModel7D = SimpleEvolutionModel<Point7D, Coord7D, Domain7D>
    type SimpleEvolutionModel8D = SimpleEvolutionModel<Point8D, Coord8D, Domain8D>


    // 1D versions
    let getMean1D (md : SimpleEvolutionModel1D) x =
        x.protocell.value.mean double (fun (p : Point1D) -> p.toCoord md.domain)

    let getStdDev1D (md : SimpleEvolutionModel1D) x =
        let variance = x.protocell.value.variance double (fun (p : Point1D) -> p.toCoord md.domain)
        variance.sqrt()

    // 2D versions
    let getMean2D (md : SimpleEvolutionModel2D) x =
        x.protocell.value.mean double (fun (p : Point2D) -> p.toCoord md.domain)

    let getStdDev2D (md : SimpleEvolutionModel2D) x =
        let variance = x.protocell.value.variance double (fun (p : Point2D) -> p.toCoord md.domain)
        variance.sqrt()

    // 3D versions
    let getMean3D (md : SimpleEvolutionModel3D) x =
        x.protocell.value.mean double (fun (p : Point3D) -> p.toCoord md.domain)

    let getStdDev3D (md : SimpleEvolutionModel3D) x =
        let variance = x.protocell.value.variance double (fun (p : Point3D) -> p.toCoord md.domain)
        variance.sqrt()

    // 4D versions
    let getMean4D (md : SimpleEvolutionModel4D) x =
        x.protocell.value.mean double (fun (p : Point4D) -> p.toCoord md.domain)

    let getStdDev4D (md : SimpleEvolutionModel4D) x =
        let variance = x.protocell.value.variance double (fun (p : Point4D) -> p.toCoord md.domain)
        variance.sqrt()

    // 5D versions
    let getMean5D (md : SimpleEvolutionModel5D) x =
        x.protocell.value.mean double (fun (p : Point5D) -> p.toCoord md.domain)

    let getStdDev5D (md : SimpleEvolutionModel5D) x =
        let variance = x.protocell.value.variance double (fun (p : Point5D) -> p.toCoord md.domain)
        variance.sqrt()

    // 6D versions
    let getMean6D (md : SimpleEvolutionModel6D) x =
        x.protocell.value.mean double (fun (p : Point6D) -> p.toCoord md.domain)

    let getStdDev6D (md : SimpleEvolutionModel6D) x =
        let variance = x.protocell.value.variance double (fun (p : Point6D) -> p.toCoord md.domain)
        variance.sqrt()

    // 7D versions
    let getMean7D (md : SimpleEvolutionModel7D) x =
        x.protocell.value.mean double (fun (p : Point7D) -> p.toCoord md.domain)

    let getStdDev7D (md : SimpleEvolutionModel7D) x =
        let variance = x.protocell.value.variance double (fun (p : Point7D) -> p.toCoord md.domain)
        variance.sqrt()

    // 8D versions
    let getMean8D (md : SimpleEvolutionModel8D) x =
        x.protocell.value.mean double (fun (p : Point8D) -> p.toCoord md.domain)

    let getStdDev8D (md : SimpleEvolutionModel8D) x =
        let variance = x.protocell.value.variance double (fun (p : Point8D) -> p.toCoord md.domain)
        variance.sqrt()
