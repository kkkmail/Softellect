namespace Softellect.DistributedProcessing.SolverRunner

open System
open Argu

module CommandLine =
    [<CliPrefix(CliPrefix.None)>]
    type SolverRunnerArguments =
        | [<Mandatory>] [<Unique>] [<AltCommandLine("q")>] RunQueue of Guid
        | [<Unique>] [<AltCommandLine("f")>] ForceRun of bool

    with
        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | RunQueue _ -> "specify RunQueueId."
                | ForceRun _ -> "specify true to force running without processor count limitation."
