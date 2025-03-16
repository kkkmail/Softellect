module TypeTests =

    type SparseValue<'I, 'T
            when ^I: equality
            and ^I: comparison
            and ^T: (static member ( * ) : ^T * ^T -> ^T)
            and ^T: (static member ( + ) : ^T * ^T -> ^T)
            and ^T: (static member ( - ) : ^T * ^T -> ^T)
            and ^T: (static member Zero : ^T)
            and ^T: equality
            and ^T: comparison> =
        {
            x : 'I
            value : 'T
        }

        member inline r.convert converter = { x = r.x; value = converter r.value }


    type SparseArray<'I, 'T
            when ^I: equality
            and ^I: comparison
            and ^T: (static member ( * ) : ^T * ^T -> ^T)
            and ^T: (static member ( + ) : ^T * ^T -> ^T)
            and ^T: (static member ( - ) : ^T * ^T -> ^T)
            and ^T: (static member Zero : ^T)
            and ^T: equality
            and ^T: comparison> =
        {
            values : SparseValue<'I, 'T>[]
            map : Lazy<Map<'I, 'T>>
        }

        static member inline private createLookupMap (values: SparseValue<'I, 'T>[]) =
            values
            |> Array.map (fun v -> v.x, v.value)
            |> Map.ofArray

        static member inline create v =
            // Remove all zero values.
            let values = v |> Array.filter (fun e -> e.value <> LanguagePrimitives.GenericZero<'T>)

            {
                values = values
                map = new Lazy<Map<'I, 'T>>(fun () -> SparseArray.createLookupMap values)
            }

        static member inline empty = SparseArray<'I, 'T>.create [||]

        member inline r.convert converter =
            r.values |> Array.map (fun v -> v.convert converter) |> SparseArray.create

        member inline r.moment (converter : 'T -> 'V) (projector : 'I -> 'C ) (n : int) : 'C =
            let c = r.values |> Array.map (fun v -> v.convert converter)
            let x0 = c |> Array.sumBy _.value

            if x0 > LanguagePrimitives.GenericZero<'V>
            then
                let xn =
                    c
                    |> Array.map (fun v -> (pown (projector v.x) n) * v.value)
                    |> Array.sum

                xn / x0
            else LanguagePrimitives.GenericZero<'C>

        member inline r.mean (converter : 'T -> 'V) (projector : 'I -> 'C ) : 'C =
            let m1 = r.moment converter projector 1
            m1


    type Domain =
        {
            points : double[]
        }


    type Domain2D =
        {
            d0 : Domain
            d1 : Domain
        }


    type Coord2D =
        {
            x0 : double
            x1 : double
        }

        static member Zero = { x0 = 0.0; x1 = 0.0 }
        static member One = { x0 = 1.0; x1 = 1.0 }
        static member (+) (a : Coord2D, b : Coord2D) = { x0 = a.x0 + b.x0; x1 = a.x1 + b.x1 }
        static member (-) (a : Coord2D, b : Coord2D) = { x0 = a.x0 - b.x0; x1 = a.x1 - b.x1 }
        static member (*) (a : Coord2D, b : Coord2D) = { x0 = a.x0 * b.x0; x1 = a.x1 * b.x1 }
        static member (*) (d : double, a : Coord2D) = { x0 = d * a.x0; x1 = d * a.x1 }
        static member (*) (a : Coord2D, d : double) = d * a
        static member (/) (a : Coord2D, b : Coord2D) = { x0 = a.x0 / b.x0; x1 = a.x1 / b.x1 }
        static member (/) (a : Coord2D, d : double) = a * (1.0 / d)


    type Point2D =
        {
            i0 : int
            i1 : int
        }

        member p.toCoord (d : Domain2D) =
            {
                x0 = d.d0.points[p.i0]
                x1 = d.d1.points[p.i1]
            }


    type SubstanceData<'I when ^I: equality and ^I: comparison> =
        {
            substance : SparseArray<'I, int64>
            // Some more data, which is irrelevant for this example.
        }


    type Model<'I, 'C, 'D
            when ^I: equality
            and ^I: comparison
            and ^I: (member toCoord : ^D -> ^C)

            // and ^C: equality
            // and ^C: comparison
            // and ^C: (static member ( + ) : ^C * ^C -> ^C)
            // and ^C: (static member ( - ) : ^C * ^C -> ^C)
            // and ^C: (static member ( * ) : ^C * ^C -> ^C)
            // and ^C: (static member ( * ) : ^C * double -> ^C)
            // and ^C: (static member ( * ) : double * ^C -> ^C)
            // and ^C: (static member ( / ) : ^C * double -> ^C)
            // and ^C: (static member op_Multiply : ^C * double -> ^C)
            // and ^C: (static member op_Division : ^C * double -> ^C)
            // and ^C: (static member Zero : ^C)
            // and ^C: (static member One : ^C)
                                                > =
        {
            domain : 'D
            // Some more data, which is irrelevant for this example.
        }

        // member inline md.mean (x : SubstanceData<^I>) : ^C =
        //     x.substance.mean double (fun (p : ^I) -> p.toCoord md.domain)


    type Model2D = Model<Point2D, Coord2D, Domain2D>


    let getMean2D (md : Model2D) x =
        x.substance.mean double (fun (p : Point2D) -> p.toCoord md.domain)
