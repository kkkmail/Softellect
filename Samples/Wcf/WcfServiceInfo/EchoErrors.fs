namespace Softellect.Samples.Wcf.WcfServiceInfo

open Softellect.Sys.Errors

module EchoErrors =

    type EchoError =
        | EchoError


    type UnitResult = UnitResult<EchoError>
    type EchoResult<'T> = StlResult<'T, EchoError>
