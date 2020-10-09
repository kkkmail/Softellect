namespace Softellect.Samples.Wcf.ServiceInfo

open Softellect.Sys.WcfErrors
open Softellect.Sys.Errors

module EchoWcfErrors =

    type EchoWcfError =
        | EchoWcfErr of WcfError


    type UnitResult = UnitResult<EchoWcfError>
    type EchoWcfResult<'T> = ResultWithErr<'T, EchoWcfError>
