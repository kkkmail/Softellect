namespace Softellect.DistributedProcessing.WorkerNodeService

open System
open System.Net
open Argu

open Softellect.Sys.Primitives
open Softellect.Messaging.Primitives
open Softellect.Messaging.AppSettings
open Softellect.Messaging.Errors
open Softellect.Sys.AppSettings
open Softellect.Wcf.Common

open Softellect.DistributedProcessing.Primitives
open Softellect.DistributedProcessing.AppSettings
//open ClmSys.ClmWorker
//open WorkerNodeServiceInfo.ServiceInfo
open Softellect.Sys.Worker
open Softellect.Sys

module CommandLine =

    [<CliPrefix(CliPrefix.Dash)>]
    type WorkerNodeServiceRunArgs =
        | [<Unique>] [<AltCommandLine("-address")>] WrkSvcAddress of string
        | [<Unique>] [<AltCommandLine("-port")>] WrkSvcPort of int
        | [<Unique>] [<AltCommandLine("-n")>] WrkName of string
        | [<Unique>] [<AltCommandLine("-c")>] WrkNoOfCores of int

        | [<Unique>] [<AltCommandLine("-save")>] WrkSaveSettings

        | [<Unique>] [<AltCommandLine("-msgAddress")>] WrkMsgSvcAddress of string
        | [<Unique>] [<AltCommandLine("-msgPort")>] WrkMsgSvcPort of int

        | [<Unique>] [<AltCommandLine("-id")>] WrkMsgCliId of Guid
        | [<Unique>] [<AltCommandLine("-p")>] WrkPartitioner of Guid
        | [<Unique>] [<AltCommandLine("-i")>] WrkInactive of bool
        | [<Unique>] [<AltCommandLine("-f")>] WrkForce of bool

    with
        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | WrkSvcAddress _ -> "worker node service ip address / name."
                | WrkSvcPort _ -> "worker node service port."
                | WrkName _ -> "worker node name."
                | WrkNoOfCores _ -> "number of processor cores used by current node. If nothing is specified, then half of available logical cores will be used."

                | WrkSaveSettings -> "saves settings into config file."

                | WrkMsgSvcAddress _ -> "messaging server ip address / name."
                | WrkMsgSvcPort _ -> "messaging server port."

                | WrkMsgCliId _ -> "messaging client id of current worker node service."
                | WrkPartitioner _ -> "messaging client id of a partitioner service."
                | WrkInactive _ -> "if true then worker node is inactive and it will unregister itself from the cluster."
                | WrkForce _ -> "if true then forces to accept parameters, which otherwise would've been corrected by the system."


    type WorkerNodeServiceArgs = WorkerArguments<WorkerNodeServiceRunArgs>

    and
        [<CliPrefix(CliPrefix.None)>]
        WorkerNodeServiceArguArgs =
        | [<Unique>] [<First>] [<AltCommandLine("r")>] Run of ParseResults<WorkerNodeServiceRunArgs>
        | [<Unique>] [<First>] [<AltCommandLine("s")>] Save of ParseResults<WorkerNodeServiceRunArgs>

    with
        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | Run _ -> "run worker node service from command line."
                | Save _ -> "save parameters into config file."


    let convertArgs s =
        match s with
        | Run a -> WorkerNodeServiceArgs.Run a
        | Save a -> WorkerNodeServiceArgs.Save a


    let tryGetSaveSettings p = p |> List.tryPick (fun e -> match e with | WrkSaveSettings -> Some () | _ -> None)


    let private proxy =
        {
            tryGetClientId = fun p -> p |> List.tryPick (fun e -> match e with | WrkMsgCliId p -> p |> MessagingClientId |> WorkerNodeId |> Some | _ -> None)
            tryGetNodeName = fun p -> p |> List.tryPick (fun e -> match e with | WrkName p -> p |> WorkerNodeName |> Some | _ -> None)
            tryGetPartitioner = fun p -> p |> List.tryPick (fun e -> match e with | WrkPartitioner p -> p |> MessagingClientId |> PartitionerId |> Some | _ -> None)
            tryGetNoOfCores = fun p -> p |> List.tryPick (fun e -> match e with | WrkNoOfCores p -> Some p | _ -> None)
            tryGetInactive = fun p -> p |> List.tryPick (fun e -> match e with | WrkInactive p -> Some p | _ -> None)
            tryGetServiceAddress = fun p -> p |> List.tryPick (fun e -> match e with | WrkSvcAddress s -> s |> ServiceAddress.tryCreate | _ -> None)
            tryGetServicePort = fun p -> p |> List.tryPick (fun e -> match e with | WrkSvcPort p -> p |> ServicePort |> Some | _ -> None)
            tryGetMsgServiceAddress = fun p -> p |> List.tryPick (fun e -> match e with | WrkMsgSvcAddress s -> s |> ServiceAddress.tryCreate | _ -> None)
            tryGetMsgServicePort = fun p -> p |> List.tryPick (fun e -> match e with | WrkMsgSvcPort p -> p |> ServicePort |> Some | _ -> None)
            tryGetForce = fun p -> p |> List.tryPick (fun e -> match e with | WrkForce p -> Some p | _ -> None)
        }


    let tryLoadWorkerNodeSettings dataVersion messagingDataVersion nodeIdOpt nameOpt =
        let providerRes = AppSettingsProvider.tryCreate AppSettingsFile
        let workerNodeSvcInfo = loadWorkerNodeServiceSettings providerRes dataVersion
        let messagingSvcInfo = loadMessagingSettings providerRes messagingDataVersion

        match tryLoadWorkerNodeInfo providerRes nodeIdOpt nameOpt with
        | Some info ->
            let w =
                {
                    workerNodeInfo = info
                    workerNodeSvcInfo = workerNodeSvcInfo
                    messagingSvcInfo = messagingSvcInfo
                }

            Some w
        | None -> None


    let tryLoadSettings dataVersion messagingDataVersion (proxy : WorkerNodeSettingsProxy<'P>) (p : 'P) =
        let workerNodeId = proxy.tryGetClientId p
        let workerNodeName = proxy.tryGetNodeName p

        match tryLoadWorkerNodeSettings dataVersion messagingDataVersion workerNodeId workerNodeName with
        | Some w ->
            //let wn = w.workerNodeSvcInfo.value.netTcpServiceInfo
            //let mn = w.messagingSvcInfo.messagingServiceAccessInfo.netTcpServiceInfo

            //let w1 =
            //    {
            //        workerNodeInfo =
            //            { w.workerNodeInfo with
            //                partitionerId = proxy.tryGetPartitioner p |> Option.defaultValue w.workerNodeInfo.partitionerId

            //                noOfCores =
            //                    let n = proxy.tryGetNoOfCores p |> Option.defaultValue w.workerNodeInfo.noOfCores
            //                    max 0 (min n (8 * Environment.ProcessorCount))

            //                nodePriority =
            //                    match w.workerNodeInfo.nodePriority.value with
            //                    | x when x <= 0 -> WorkerNodePriority.defaultValue
            //                    | _ -> w.workerNodeInfo.nodePriority

            //                isInactive = proxy.tryGetInactive p |> Option.defaultValue w.workerNodeInfo.isInactive
            //                lastErrorDateOpt = w.workerNodeInfo.lastErrorDateOpt
            //            }

            //        workerNodeSvcInfo =
            //            { w.workerNodeSvcInfo.value with
            //                netTcpServiceInfo =
            //                    { wn with
            //                        netTcpServiceAddress = proxy.tryGetServiceAddress p |> Option.defaultValue wn.netTcpServiceAddress
            //                        netTcpServicePort = proxy.tryGetServicePort p |> Option.defaultValue wn.netTcpServicePort
            //                    }
            //            }
            //            |> WorkerNodeServiceAccessInfo

            //        workerNodeCommunicationType = w.workerNodeCommunicationType

            //        messagingSvcInfo =
            //            { w.messagingSvcInfo with
            //                messagingServiceAccessInfo =
            //                    { w.messagingSvcInfo.messagingServiceAccessInfo with
            //                        netTcpServiceInfo =
            //                            { mn with
            //                                netTcpServiceAddress = proxy.tryGetMsgServiceAddress p |> Option.defaultValue mn.netTcpServiceAddress
            //                                netTcpServicePort = proxy.tryGetMsgServicePort p |> Option.defaultValue mn.netTcpServicePort
            //                            }
            //                    }
            //                messagingDataVersion = messagingDataVersion
            //            }

            //        messagingCommunicationType = w.messagingCommunicationType
            //    }

            //Some w1
            failwith "tryLoadWorkerNodeSettings is not implemented yet."
        | None -> None


    let updateWorkerNodeServiceSettings (provider : AppSettingsProvider) (w : WorkerNodeServiceAccessInfo) (ct : WcfCommunicationType)  =
        //let h = w.value.httpServiceInfo
        //let n = w.value.netTcpServiceInfo

        //provider.trySet workerNodeServiceAddressKey n.netTcpServiceAddress.value |> ignore
        //provider.trySet workerNodeServiceHttpPortKey h.httpServicePort.value |> ignore
        //provider.trySet workerNodeServiceNetTcpPortKey n.netTcpServicePort.value |> ignore
        //provider.trySet workerNodeServiceCommunicationTypeKey ct.value |> ignore
        failwith "updateWorkerNodeServiceSettings is not implemented yet."


    type WorkerNodeSettings
        with
        member w.trySaveSettings() =
            let toErr e = e |> MsgSettingExn |> Error

            match w.isValid(), AppSettingsProvider.tryCreate AppSettingsFile with
            | Ok(), Ok provider ->
                let v = w.workerNodeInfo
                //let wh = w.workerNodeSvcInfo.value.httpServiceInfo
                //let wn = w.workerNodeSvcInfo.value.netTcpServiceInfo

                try
                    //provider.trySet workerNodeNameKey v.workerNodeName.value |> ignore
                    //provider.trySet workerNodeIdKey v.workerNodeId.value.value |> ignore
                    //provider.trySet noOfCoresKey v.noOfCores |> ignore
                    //provider.trySet partitionerIdKey v.partitionerId.value |> ignore
                    //provider.trySet isInactiveKey v.isInactive |> ignore
                    //provider.trySet nodePriorityKey v.nodePriority.value |> ignore

                    //updateWorkerNodeServiceSettings provider w.workerNodeSvcInfo w.workerNodeCommunicationType
                    //updateMessagingSettings provider w.messagingSvcInfo w.messagingCommunicationType

                    //provider.trySave() |> Rop.bindError toErr
                    failwith "trySaveSettings is not implemented yet."
                with
                | e -> toErr e
            | Error e, _ -> Error e
            | _, Error e -> toErr e



    let getWorkerNodeServiceAccessInfo (loadSettings, tryGetSaveSettings) b =
        let w = loadSettings()
        printfn $"getServiceAccessInfoImpl: w1 = %A{w}"

        let g() =
            {
                workerNodeInfo = w.workerNodeInfo
                workerNodeServiceAccessInfo = w.workerNodeSvcInfo
                messagingServiceAccessInfo =  w.messagingSvcInfo
            }

        let r =
            match tryGetSaveSettings(), b with
            | Some(), _ -> w.trySaveSettings()
            | _, true -> w.trySaveSettings()
            | _ -> w.isValid()

        printfn $"getServiceAccessInfoImpl: r = %A{r}"

        match r with
        | Ok() -> g() |> Ok
        | Error e -> Error e


    let loadSettings messagingDataVersion p =
        //match tryLoadSettings messagingDataVersion proxy p with
        //| Some w ->
        //    printfn $"loadSettings: w = %A{w}"
        //    w
        //| None -> invalidOp "Unable to load settings."
        failwith "loadSettings is not implemented yet."


    let getServiceAccessInfoImpl messagingDataVersion b p =
        let load() = loadSettings messagingDataVersion p
        let trySave() = tryGetSaveSettings p
        getWorkerNodeServiceAccessInfo (load, trySave) b


    let getServiceAccessInfo messagingDataVersion p = getServiceAccessInfoImpl messagingDataVersion false p
    let saveSettings messagingDataVersion p = getServiceAccessInfoImpl messagingDataVersion true p |> ignore
