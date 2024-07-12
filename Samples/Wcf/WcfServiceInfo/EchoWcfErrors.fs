namespace Softellect.Samples.Wcf.ServiceInfo

open Softellect.Wcf.Errors
open Softellect.Sys.Rop

module EchoWcfErrors =

    type EchoWcfError =
        | EchoWcfErr of WcfError


    type UnitResult = UnitResult<EchoWcfError>
    type EchoWcfResult<'T> = Result<'T, EchoWcfError>
