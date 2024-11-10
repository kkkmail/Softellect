namespace Softellect.Samples.DistrProc.ModelGenerator

open Argu

module CommandLine =

    let ProgramName = "ModelGenerator"

    [<CliPrefix(CliPrefix.Dash)>]
    type ModelGeneratorArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-i")>] SeedValue of int
        |               [<Unique>] [<AltCommandLine("-d")>] Delay of int
        |               [<Unique>] [<AltCommandLine("-r")>] RunTime of int
        |               [<Unique>] [<AltCommandLine("-m")>] ModelId of int

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | SeedValue _ -> "seed value."
                | Delay _ -> "delay in milliseconds per each derivative call to test cancellations."
                | RunTime _ -> "run time of hte model (default is 1,000)."
                | ModelId _ -> "model id to run: 1 - lotkaVolterra (default), 2 - dampedHarmonicOscillator, 3 - lorenzSystem."
