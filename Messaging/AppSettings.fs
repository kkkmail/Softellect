namespace Softellect.Messaging

open System
open Softellect.Sys.Logging
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
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let d = MessagingServiceAccessInfo.defaultValue messagingDataVersion
            let m = getServiceAccessInfo provider messagingServiceAccessInfoKey d.serviceAccessInfo
            let expirationTimeInMinutes = provider.getIntOrDefault expirationTimeInMinutesKey defaultExpirationTime.Minutes |> int64 |> TimeSpan.FromMinutes

            let messagingSvcInfo =
                {
                    messagingDataVersion = messagingDataVersion
                    serviceAccessInfo = m
                    expirationTime = expirationTimeInMinutes
                }

            messagingSvcInfo
        | Error e ->
            Logger.logCrit $"loadMessagingServiceAccessInfo - Cannot load settings. Error: '%A{e}'."
            failwith $"loadMessagingServiceAccessInfo - Cannot load settings. Error: '%A{e}'."


    let updateMessagingServiceAccessInfo (m : MessagingServiceAccessInfo) =
        let toErr e = e |> MsgSettingExn |> MsgSettingsErr |> Error

        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let s = m.serviceAccessInfo
            Logger.logTrace (fun () -> $"updateMessagingSettings - s: '{s}'.")
            let v = s.serialize()
            Logger.logTrace (fun () -> $"updateMessagingSettings - v: '{v}'.")
            let result = provider.trySet messagingServiceAccessInfoKey v
            Logger.logTrace (fun () -> $"updateMessagingSettings - result: '%A{result}'.")
            provider.trySet expirationTimeInMinutesKey (int m.expirationTime.TotalMinutes) |> ignore
            provider.trySave() |> Rop.bindError toErr
        | Error e ->
            Logger.logError $"updateMessagingServiceAccessInfo: ERROR - %A{e}."
            e |> FileErr |> MsgSettingsErr |> Error
