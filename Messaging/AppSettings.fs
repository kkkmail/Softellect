namespace Softellect.Messaging

open System
open Softellect.Sys.Core
open Softellect.Wcf.Common
open Softellect.Wcf.AppSettings
open Softellect.Messaging.Errors
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Sys.Primitives
open Softellect.Sys.AppSettings
open Softellect.Messaging.VersionInfo
open Softellect.Messaging.Primitives
open Softellect.Sys


module AppSettings =

    let expirationTimeInMinutesKey = ConfigKey "ExpirationTimeInMinutes"
    let messagingServiceAccessInfoKey = ConfigKey "MessagingServiceAccessInfo"


    let updateMessagingServiceAccessInfo (provider : AppSettingsProvider) (m : MessagingServiceAccessInfo) =
        let toErr e = e |> MsgSettingExn |> MsgSettingsErr |> Error
        let s = m.serviceAccessInfo
        printfn $"updateMessagingSettings - s: '{s}'."
        let v = s.serialize()
        printfn $"updateMessagingSettings - v: '{v}'."
        let result = provider.trySet messagingServiceAccessInfoKey v
        printfn $"updateMessagingSettings - result: '%A{result}'."
        provider.trySet expirationTimeInMinutesKey (int m.expirationTime.TotalMinutes) |> ignore
        provider.trySave() |> Rop.bindError toErr


    let loadMessagingServiceAccessInfo messagingDataVersion =
        let providerRes = AppSettingsProvider.tryCreate AppSettingsFile
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


    //type MsgSettings =
    //    {
    //        expirationTime : TimeSpan
    //        messagingDataVersion : MessagingDataVersion
    //        messagingSvcInfo : MessagingServiceAccessInfo
    //    }

    //    member w.isValid() = Ok()
    //        //let h = w.messagingSvcInfo.messagingServiceAccessInfo.httpServiceInfo

    //        //let r =
    //        //    [
    //        //        h.httpServiceAddress.value <> EmptyString, $"%A{h.httpServiceAddress} is invalid"
    //        //        h.httpServicePort.value > 0, $"%A{h.httpServicePort.value} is invalid"
    //        //    ]
    //        //    |> List.fold(fun acc r -> combine acc r) (true, EmptyString)

    //        //match r with
    //        //| true, _ -> Ok()
    //        //| false, s -> s |> InvalidSettings |> MsgSettingsErr |> Error

    //    member w.trySaveSettings() =
    //        let toErr e = e |> MsgSettingExn |> MsgSettingsErr |> Error

    //        let result =
    //            match w.isValid(), AppSettingsProvider.tryCreate AppSettingsFile with
    //            | Ok(), Ok provider ->
    //                try
    //                    printfn "Calling updateMessagingSettings..."
    //                    updateMessagingSettings provider w.messagingSvcInfo |> ignore
    //                    provider.trySet expirationTimeInMinutesKey (int w.expirationTime.TotalMinutes) |> ignore
    //                    provider.trySave() |> Rop.bindError toErr
    //                with
    //                | e ->
    //                    printfn $"trySaveSettings - error: '{e}'."
    //                    toErr e
    //            | Error e, _ -> Error e
    //            | _, Error e -> toErr e

    //        printfn $"trySaveSettings - result '%A{result}'."
    //        result


    //type MsgServiceSettingsProxy<'P> =
    //    {
    //        tryGetMsgServiceAddress : 'P -> ServiceAddress option
    //        tryGetMsgServicePort : 'P -> ServicePort option
    //    }


    //let loadMsgServiceSettings messagingDataVersion =
    //    let providerRes = AppSettingsProvider.tryCreate AppSettingsFile
    //    let messagingSvcInfo = loadMessagingSettings providerRes messagingDataVersion

    //    let expirationTimeInMinutes =
    //        match providerRes with
    //        | Ok provider ->
    //            match provider.tryGetInt expirationTimeInMinutesKey with
    //            | Ok (Some n) when n > 0 -> TimeSpan.FromMinutes(float n)
    //            | _ -> defaultExpirationTime
    //        | _ -> defaultExpirationTime

    //    let w =
    //        {
    //            expirationTime = expirationTimeInMinutes
    //            messagingDataVersion = messagingDataVersion
    //            messagingSvcInfo = messagingSvcInfo
    //        }

    //    printfn $"loadMsgServiceSettings - w = %A{w}"
    //    w


    //let loadSettingsImpl (proxy : MsgServiceSettingsProxy<'P>) messagingDataVersion p =
    //    printfn "loadSettingsImpl - ERROR - DOES NOT SUPPORT PARSING COMMAND LINE PARAMETERS. FIX."
    //    let w = loadMsgServiceSettings messagingDataVersion
    //    //let h = w.messagingSvcInfo.messagingServiceAccessInfo.httpServiceInfo
    //    //let n = w.messagingSvcInfo.messagingServiceAccessInfo.netTcpServiceInfo
    //
    //    //let serviceAddress = proxy.tryGetMsgServiceAddress p |> Option.defaultValue h.httpServiceAddress
    //    //let netTcpServicePort = proxy.tryGetMsgServicePort p |> Option.defaultValue n.netTcpServicePort
    //    //let httpServiceInfo = HttpServiceAccessInfo.create serviceAddress h.httpServicePort h.httpServiceName
    //    //let netTcpServiceInfo = NetTcpServiceAccessInfo.create serviceAddress netTcpServicePort n.netTcpServiceName WcfSecurityMode.defaultValue
    //    //let msgServiceAccessInfo = ServiceAccessInfo.create httpServiceInfo netTcpServiceInfo
    //    //let messagingSvcInfo = MessagingServiceAccessInfo.create messagingDataVersion msgServiceAccessInfo
    //
    //    //let w1 = { w with messagingSvcInfo = messagingSvcInfo }
    //    //printfn $"loadSettingsImpl - w1 = %A{w1}"
    //    //w1
    //    w


    //let getMsgServiceInfo (loadSettings, tryGetSaveSettings) b =
    //    let (w : MsgSettings) = loadSettings()
    //    printfn $"getMsgServiceInfo: w = %A{w}"

    //    let r =
    //        match tryGetSaveSettings(), b with
    //        | Some _, _ -> w.trySaveSettings()
    //        | _, true -> w.trySaveSettings()
    //        | _ -> Ok()

    //    match r with
    //    | Ok() -> printfn "Successfully saved settings."
    //    | Error e -> printfn $"Error occurred trying to save settings: %A{e}."

    //    w
