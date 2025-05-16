namespace Softellect.Analytics

module Primitives =

    type DataPoint2D =
        {
            x : double
            y : double
        }


    type DataPoint3D =
        {
            x : double
            y : double
            z : double
        }


    type DataLabel3D =
        {
            xLabel : string
            yLabel : string
            zLabel : string
        }

        static member defaultValue =
            {
                xLabel = "x"
                yLabel = "y"
                zLabel = "z"
            }


    type DataLabel =
        | DataLabel of string

        member this.value = let (DataLabel v) = this in v


    type DataSeries2D =
        {
            dataLabel : DataLabel
            dataPoints : DataPoint2D list
        }
