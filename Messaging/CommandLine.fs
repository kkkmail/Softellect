namespace Softellect.Messaging

open Argu
open System.Net

open Softellect.Sys.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Messaging.AppSettings
open Softellect.Sys.Worker
open Softellect.Messaging.DataAccess
open Softellect.Messaging.ServiceProxy
open Softellect.Messaging.Primitives

module CommandLine =

    [<CliPrefix(CliPrefix.Dash)>]
    type MessagingServiceRunArgs =
        | [<Unique>] [<AltCommandLine("-address")>] MsgSvcAddress of string
        | [<Unique>] [<AltCommandLine("-port")>] MsgSvcPort of int
        | [<Unique>] [<AltCommandLine("-save")>] MsgSaveSettings

    with
        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | MsgSvcAddress _ -> "messaging server ip address / name."
                | MsgSvcPort _ -> "messaging server port."
                | MsgSaveSettings -> "saves settings into config file."


    type MsgSvcArgs = WorkerArguments<MessagingServiceRunArgs>

    and
        [<CliPrefix(CliPrefix.None)>]
        MsgSvcArguArgs =
        | [<Unique>] [<First>] [<AltCommandLine("r")>] Run of ParseResults<MessagingServiceRunArgs>
        | [<Unique>] [<First>] [<AltCommandLine("s")>] Save of ParseResults<MessagingServiceRunArgs>

    with
        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | Run _ -> "run messaging service from command line without installing."
                | Save _ -> "save parameters into the config file."


    let convertArgs s =
        match s with
        | Run a -> MsgSvcArgs.Run a
        | Save a -> MsgSvcArgs.Save a


    let tryGetSaveSettings p = p |> List.tryPick (fun e -> match e with | MsgSaveSettings -> Some () | _ -> None)

    let private proxy =
        {
            tryGetMsgServiceAddress = fun p -> p |> List.tryPick (fun e -> match e with | MsgSvcAddress s -> s |> ServiceAddress.tryCreate | _ -> None)
            tryGetMsgServicePort = fun p -> p |> List.tryPick (fun e -> match e with | MsgSvcPort p -> p |> ServicePort |> Some | _ -> None)
        }

    let loadSettings p = loadSettingsImpl proxy p


    let getServiceSettingsImpl b v p =
        let load() = loadSettings v p
        let tryGetSave() = tryGetSaveSettings p
        getMsgServiceInfo (load, tryGetSave) b


    let getServiceSettings = getServiceSettingsImpl false
    let saveSettings v p = getServiceSettingsImpl true v p |> ignore


    type MessagingConfigParam
        with
        static member fromParseResults (p : ParseResults<MessagingServiceRunArgs>) : list<MessagingConfigParam> =
            [
            ]
            |> List.choose id


    let getParams v p = MessagingConfigParam.fromParseResults p, getServiceSettings v (p.GetAllResults())
    let getSaveSettings v (p : ParseResults<MessagingServiceRunArgs>) () = p.GetAllResults() |> saveSettings v
    type MessagingServiceTask = WorkerTask<(list<MessagingConfigParam> * MsgSettings), MessagingServiceRunArgs>


    let getMessagingServiceDataImpl<'D> logger proxy v =
        let i = getServiceSettings v []

        let serviceData =
            {
                expirationTime = i.expirationTime
                messagingDataVersion = v
                messagingServiceProxy = proxy
            }

        let msgServiceData = getMsgServiceData i.messagingSvcInfo.messagingServiceAccessInfo logger serviceData
        printfn $"tryGetMessagingServiceDataImpl: msgServiceDataRes = %A{msgServiceData}"
        msgServiceData


    let getMessagingServiceData<'D> logger (v : MessagingDataVersion) =
        let proxy = createMessagingServiceProxy getMessagingConnectionString v
        getMessagingServiceDataImpl<'D> logger proxy v
