namespace Softellect.MessagingService

open Argu

open Softellect.Sys.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Sys.Logging
open Softellect.Sys.Errors
open Softellect.Messaging.Service
open Softellect.Messaging.Settings
open Softellect.Wcf.Errors
open Softellect.Wcf.Service
open Softellect.Sys.Worker
open Softellect.Messaging.VersionInfo
open Softellect.Messaging.DataAccess
open Softellect.Messaging.ServiceProxy


//open ClmSys.MessagingData
//open MessagingServiceInfo.ServiceInfo

//open ServiceProxy.MsgServiceProxy
//open DbData.Configuration

module SvcCommandLine =

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
            tryGetMsgServiceAddress = fun p -> p |> List.tryPick (fun e -> match e with | MsgSvcAddress s -> s |> ServiceAddress |> Some | _ -> None)
            tryGetMsgServicePort = fun p -> p |> List.tryPick (fun e -> match e with | MsgSvcPort p -> p |> ServicePort |> Some | _ -> None)
        }

    let loadSettings p = loadSettingsImpl proxy p


    let getServiceSettingsImpl b p =
        let load() = loadSettings p
        let tryGetSave() = tryGetSaveSettings p
        getMsgServiceInfo (load, tryGetSave) b


    let getServiceSettings = getServiceSettingsImpl false
    let saveSettings p = getServiceSettingsImpl true p |> ignore


    type MessagingConfigParam
        with
        static member fromParseResults (p : ParseResults<MessagingServiceRunArgs>) : list<MessagingConfigParam> =
            [
            ]
            |> List.choose id


    let getParams p = MessagingConfigParam.fromParseResults p, getServiceSettings (p.GetAllResults())
    let getSaveSettings (p : ParseResults<MessagingServiceRunArgs>) () = p.GetAllResults() |> saveSettings
    type MessagingServiceTask = WorkerTask<(list<MessagingConfigParam> * MsgSettings), MessagingServiceRunArgs>


    let tryGetMessagingServiceDataImpl<'D> logger proxy : WcfServiceDataResult<'D> =
        let i = getServiceSettings []

        let serviceData =
            {
                messagingServiceInfo =
                    {
                        expirationTime = i.messagingInfo.expirationTime
                        messagingDataVersion = messagingDataVersion
                    }

                messagingServiceProxy = proxy
                communicationType = i.communicationType
            }

        let msgServiceDataRes = tryGetMsgServiceData i.messagingSvcInfo.messagingServiceAccessInfo logger serviceData
        msgServiceDataRes


    let getMessagingServiceData<'D> logger =
        let proxy = createMessagingServiceProxy getMessagingConnectionString
        Lazy<WcfServiceDataResult<'D>>(fun () -> tryGetMessagingServiceDataImpl<'D> logger proxy)
