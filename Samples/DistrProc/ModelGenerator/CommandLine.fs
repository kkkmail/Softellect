namespace Softellect.Samples.DistrProc.ModelGenerator

open Argu

module CommandLine =

    let ProgramName = "ModelGenerator"

    [<CliPrefix(CliPrefix.Dash)>]
    type ModelGeneratorArgs =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("-i")>] SeedValue of int
        |               [<Unique>] [<AltCommandLine("-d")>] Delay of int

        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | SeedValue _ -> "seed value."
                | Delay _ -> "delay in milliseconds per each derivative call to test cancellations."
