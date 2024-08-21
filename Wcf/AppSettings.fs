namespace Softellect.Wcf

open Softellect.Sys.AppSettings
open Softellect.Wcf.Common

module AppSettings =

    let getServiceAccessInfo (providerRes : AppSettingsProviderResult) n d =
        match providerRes with
        | Ok provider -> provider.tryGetOrDefault d ServiceAccessInfo.tryDeserialize n
        | _ -> d
