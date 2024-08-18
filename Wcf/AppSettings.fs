namespace Softellect.Wcf

open Softellect.Sys.AppSettings
open Softellect.Wcf.Common

module AppSettings =

    //let getCommunicationType (providerRes : AppSettingsProviderResult) n d =
    //    match providerRes with
    //    | Ok provider ->
    //        match provider.tryGetString n with
    //        | Ok (Some s) -> WcfCommunicationType.tryCreate s |> Option.defaultValue (NetTcpCommunication NoSecurity)
    //        | _ -> d
    //    | _ -> d


    let getServiceAccessInfo (providerRes : AppSettingsProviderResult) n d =
        match providerRes with
        //| Ok provider -> provider.tryGetFromJsonOrDefault<ServiceAccessInfo> d n
        | Ok provider -> provider.tryGetOrDefault d ServiceAccessInfo.tryDeserialize n
        | _ -> d
