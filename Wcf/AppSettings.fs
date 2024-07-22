namespace Softellect.Wcf

open Softellect.Sys.AppSettings
open Softellect.Wcf.Common

module AppSettings =

    let getCommunicationType (providerRes : AppSettingsProviderResult) n d =
        match providerRes with
        | Ok provider ->
            match provider.tryGetString n with
            | Ok (Some s) -> WcfCommunicationType.tryCreate s |> Option.defaultValue NetTcpCommunication
            | _ -> d
        | _ -> d
