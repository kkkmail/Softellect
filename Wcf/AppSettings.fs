namespace Softellect.Wcf

open Softellect.Sys.AppSettings
open Softellect.Wcf.Common

module AppSettings =

    let getServiceAccessInfo (provider : AppSettingsProvider) n d =
        provider.getOrDefault d ServiceAccessInfo.tryDeserialize n
