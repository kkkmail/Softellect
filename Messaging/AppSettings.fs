namespace Softellect.Messaging

open System
open Softellect.Wcf.AppSettings
open Softellect.Messaging.Errors
open Softellect.Messaging.ServiceInfo
open Softellect.Sys.Primitives
open Softellect.Sys.AppSettings
open Softellect.Sys

module AppSettings =

    let expirationTimeInMinutesKey = ConfigKey "ExpirationTimeInMinutes"
    let messagingServiceAccessInfoKey = ConfigKey "MessagingServiceAccessInfo"


    let loadMessagingServiceAccessInfo messagingDataVersion =
        let providerRes = AppSettingsProvider.tryCreate appSettingsFile
        let d = MessagingServiceAccessInfo.defaultValue messagingDataVersion
        let m = getServiceAccessInfo providerRes messagingServiceAccessInfoKey d.serviceAccessInfo

        let expirationTimeInMinutes =
            match providerRes with
            | Ok provider ->
                match provider.tryGetInt expirationTimeInMinutesKey with
                | Ok (Some n) when n > 0 -> TimeSpan.FromMinutes(float n)
                | _ -> defaultExpirationTime
            | _ -> defaultExpirationTime

        let messagingSvcInfo =
            {
                messagingDataVersion = messagingDataVersion
                serviceAccessInfo = m
                expirationTime = expirationTimeInMinutes
            }

        messagingSvcInfo


    let updateMessagingServiceAccessInfo (m : MessagingServiceAccessInfo) =
        let providerRes = AppSettingsProvider.tryCreate appSettingsFile
        let toErr e = e |> MsgSettingExn |> MsgSettingsErr |> Error

        match providerRes with
        | Ok provider ->
            let s = m.serviceAccessInfo
            printfn $"updateMessagingSettings - s: '{s}'."
            let v = s.serialize()
            printfn $"updateMessagingSettings - v: '{v}'."
            let result = provider.trySet messagingServiceAccessInfoKey v
            printfn $"updateMessagingSettings - result: '%A{result}'."
            provider.trySet expirationTimeInMinutesKey (int m.expirationTime.TotalMinutes) |> ignore
            provider.trySave() |> Rop.bindError toErr
        | Error e -> toErr e
