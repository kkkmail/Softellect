namespace Softellect.Samples.Wcf.WcfServiceInfo

open Softellect.Sys.Errors

module EchoWcfErrors =

    type EchoWcfError =
        | EchoWcfError


    type UnitResult = UnitResult<EchoWcfError>
    type EchoWcfResult<'T> = StlResult<'T, EchoWcfError>
