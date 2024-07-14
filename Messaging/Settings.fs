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


module Settings =

    type MessagingConfigParam =
        | DummyConfig


    let expirationTimeInMinutes = ConfigKey "ExpirationTimeInMinutes"
    let messagingServiceAddress = ConfigKey "MessagingServiceAddress"
    let messagingHttpServicePort = ConfigKey "MessagingHttpServicePort"
    let messagingNetTcpServicePort = ConfigKey "MessagingNetTcpServicePort"
    let messagingServiceCommunicationType = ConfigKey "MessagingServiceCommunicationType"


    let updateMessagingSettings (provider : AppSettingsProvider) (m : MessagingServiceAccessInfo) (ct : WcfCommunicationType)  =
        let mh = m.messagingServiceAccessInfo.httpServiceInfo
        let mn = m.messagingServiceAccessInfo.netTcpServiceInfo

        provider.trySet messagingServiceAddress mn.netTcpServiceAddress.value |> ignore
        provider.trySet messagingHttpServicePort mh.httpServicePort.value |> ignore
        provider.trySet messagingNetTcpServicePort mn.netTcpServicePort.value |> ignore
        provider.trySet messagingServiceCommunicationType ct.value |> ignore


    let loadMessagingSettings providerRes =
        let messagingServiceCommunicationType = getCommunicationType providerRes messagingServiceCommunicationType NetTcpCommunication
        let serviceAddress = getServiceAddress providerRes messagingServiceAddress defaultMessagingServiceAddress
        let httpServicePort = getServiceHttpPort providerRes messagingHttpServicePort defaultMessagingHttpServicePort
        let netTcpServicePort = getServiceNetTcpPort providerRes messagingNetTcpServicePort defaultMessagingNetTcpServicePort

        let h = HttpServiceAccessInfo.create serviceAddress httpServicePort messagingHttpServiceName.value
        let n = NetTcpServiceAccessInfo.create serviceAddress netTcpServicePort messagingNetTcpServiceName.value WcfSecurityMode.defaultValue
        let m = ServiceAccessInfo.create h n
        let messagingSvcInfo = MessagingServiceAccessInfo.create messagingDataVersion m

        messagingSvcInfo, messagingServiceCommunicationType


    type MsgSettings =
        {
            messagingInfo : MessagingServiceInfo
            messagingSvcInfo : MessagingServiceAccessInfo
            communicationType : WcfCommunicationType
        }

        member w.isValid() =
            let h = w.messagingSvcInfo.messagingServiceAccessInfo.httpServiceInfo

            let r =
                [
                    h.httpServiceAddress.value <> EmptyString, $"%A{h.httpServiceAddress} is invalid"
                    h.httpServicePort.value > 0, $"%A{h.httpServicePort.value} is invalid"
                ]
                |> List.fold(fun acc r -> combine acc r) (true, EmptyString)

            match r with
            | true, _ -> Ok()
            | false, s -> s |> InvalidSettings |> MsgSettingsErr |> Error

        member w.trySaveSettings() =
            let toErr e = e |> MsgSettingExn |> MsgSettingsErr |> Error

            match w.isValid(), AppSettingsProvider.tryCreate AppSettingsFile with
            | Ok(), Ok provider ->
                try
                    updateMessagingSettings provider w.messagingSvcInfo w.communicationType
                    provider.trySet expirationTimeInMinutes (int w.messagingInfo.expirationTime.TotalMinutes) |> ignore
                    provider.trySave() |> Rop.bindError toErr
                with
                | e -> toErr e
            | Error e, _ -> Error e
            | _, Error e -> toErr e


    type MsgServiceSettingsProxy<'P> =
        {
            tryGetMsgServiceAddress : 'P -> ServiceAddress option
            tryGetMsgServicePort : 'P -> ServicePort option
        }


    let loadMsgServiceSettings() =
        let providerRes = AppSettingsProvider.tryCreate AppSettingsFile
        let messagingSvcInfo, messagingServiceCommunicationType = loadMessagingSettings providerRes

        let expirationTimeInMinutes =
            match providerRes with
            | Ok provider ->
                match provider.tryGetInt expirationTimeInMinutes with
                | Ok (Some n) when n > 0 -> TimeSpan.FromMinutes(float n)
                | _ -> MessagingServiceInfo.defaultExpirationTime
            | _ -> MessagingServiceInfo.defaultExpirationTime

        let w =
            {
                messagingInfo =
                    {
                        expirationTime = expirationTimeInMinutes
                        messagingDataVersion = messagingDataVersion
                    }

                messagingSvcInfo = messagingSvcInfo
                communicationType = messagingServiceCommunicationType
            }

        w


    let loadSettingsImpl (proxy : MsgServiceSettingsProxy<'P>) p =
        let w = loadMsgServiceSettings()
        let h = w.messagingSvcInfo.messagingServiceAccessInfo.httpServiceInfo
        let n = w.messagingSvcInfo.messagingServiceAccessInfo.netTcpServiceInfo

        let serviceAddress = proxy.tryGetMsgServiceAddress p |> Option.defaultValue h.httpServiceAddress
        let netTcpServicePort = proxy.tryGetMsgServicePort p |> Option.defaultValue n.netTcpServicePort
        let httpServiceInfo = HttpServiceAccessInfo.create serviceAddress h.httpServicePort h.httpServiceName
        let netTcpServiceInfo = NetTcpServiceAccessInfo.create serviceAddress netTcpServicePort n.netTcpServiceName WcfSecurityMode.defaultValue
        let msgServiceAccessInfo = ServiceAccessInfo.create httpServiceInfo netTcpServiceInfo
        let messagingSvcInfo = MessagingServiceAccessInfo.create messagingDataVersion msgServiceAccessInfo

        let w1 = { w with messagingSvcInfo = messagingSvcInfo }

        w1


    let getMsgServiceInfo (loadSettings, tryGetSaveSettings) b =
        let (w : MsgSettings) = loadSettings()
        printfn $"getMsgServiceInfo: w = %A{w}"

        let r =
            match tryGetSaveSettings(), b with
            | Some _, _ -> w.trySaveSettings()
            | _, true -> w.trySaveSettings()
            | _ -> Ok()

        match r with
        | Ok() -> printfn "Successfully saved settings."
        | Error e -> printfn $"Error occurred trying to save settings: %A{e}."

        w
