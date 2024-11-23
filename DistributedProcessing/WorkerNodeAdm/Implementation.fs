namespace Softellect.DistributedProcessing.WorkerNodeAdm

open Softellect.DistributedProcessing.WorkerNodeAdm.CommandLine
open Softellect.DistributedProcessing.Primitives.Common
open Softellect.DistributedProcessing.Primitives.WorkerNodeAdm
open Softellect.Messaging.Primitives
open Softellect.Messaging.ServiceInfo
open Softellect.Sys
open Softellect.Sys.AppSettings
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.Sys.Crypto
open Softellect.Messaging.Client
open Softellect.DistributedProcessing.Errors
open Softellect.DistributedProcessing.DataAccess.WorkerNodeAdm
open Softellect.DistributedProcessing.VersionInfo
open Softellect.Messaging.DataAccess
open Softellect.DistributedProcessing.AppSettings.WorkerNodeAdm

module Implementation =

    let private toError g f = f |> g |> Error
    let private addError g f e = ((f |> g) + e) |> Error


    let private tryGenerateWorkerNodeKeys (w : WorkerNodeId) force =
        let g() =
            let publicKey, privateKey = generateKey (KeyId w.value.value)

            match trySaveWorkerNodePrivateKey privateKey, trySaveWorkerNodePublicKey publicKey with
            | Ok(), Ok() -> Ok()
            | Error e, Ok() -> Error e
            | Ok(), Error e -> Error e
            | Error e1, Error e2 -> e1 + e2 |> Error

        match tryLoadWorkerNodePrivateKey (), tryLoadWorkerNodePublicKey (), force with
        | Ok (Some _), Ok (Some _), false -> Ok()
        | _ -> g()


    let private tryExporWorkerNodePublicKey (folderName : FolderName) overwrite =
        let toError e = e |> TryExportWorkerNodePublicKeyErr |> Error

        match tryLoadWorkerNodePublicKey() with
        | Ok (Some key) ->
            match tryExportPublicKey folderName key overwrite with
            | Ok() -> Ok()
            | Error e -> e |> TryExpWorkerNodePublicKeyErr |> toError
        | Ok None -> NoWorkerNodePublicKeyErr |> toError
        | Error e -> Error e


    let private tryImportPartitionerPublicKey (fileName : FileName) =
        match tryImportPublicKey fileName None with
        | Ok key -> Ok key
        | Error e -> e |> TryImportWorkerNodePublicKeyErr |> TryLoadWorkerNodePublicKeyErr |> Error


    let private tryUpdatePartitionerPublicKey (p : PartitionerId) (key : PublicKey) =
        match checkKey (KeyId p.value) key with
        | true -> trySavePartitionerPublicKey key
        | false -> KeyMismatchPartitionerPublicKeyErr |> TryLoadPartitionerPublicKeyErr |> Error


    type WorkerNodeAdmProxy =
        {
            tryGenerateWorkerNodeKeys : bool -> DistributedProcessingUnitResult
            tryExportPublicKey : FolderName -> bool -> DistributedProcessingUnitResult
            tryImportPartitionerPublicKey : FileName -> DistributedProcessingResult<KeyId * PublicKey>
            tryUpdatePartitionerPublicKey : PartitionerId -> PublicKey -> DistributedProcessingUnitResult
        }

        static member create (i : WorkerNodeInfo) =
            {
                tryGenerateWorkerNodeKeys = tryGenerateWorkerNodeKeys i.workerNodeId
                tryExportPublicKey = tryExporWorkerNodePublicKey
                tryImportPartitionerPublicKey = tryImportPartitionerPublicKey
                tryUpdatePartitionerPublicKey = tryUpdatePartitionerPublicKey
            }


    type WorkerNodeAdmContext =
        {
            workerNodeAdmProxy : WorkerNodeAdmProxy
            workerNodeInfo : WorkerNodeInfo
        }

        static member create () =
            match AppSettingsProvider.tryCreate() with
            | Ok provider ->
                let w = loadWorkerNodeInfo provider

                {
                    workerNodeAdmProxy = WorkerNodeAdmProxy.create w
                    workerNodeInfo = w
                }
            | Error e -> failwith $"ERROR: {e}"


    let generateKeys (ctx : WorkerNodeAdmContext) (x : list<GenerateKeysArgs>) =
        let force = x |> List.tryPick (fun e -> match e with | Force e -> Some e | _ -> None) |> Option.defaultValue false
        let result = ctx.workerNodeAdmProxy.tryGenerateWorkerNodeKeys force
        result


    let exportPublicKey (ctx : WorkerNodeAdmContext) (x : list<ExportPublicKeyArgs>) =
        let ofn = x |> List.tryPick (fun e -> match e with | OutputFolderName e -> e |> FolderName |> Some | _ -> None)
        let o = x |> List.tryPick (fun e -> match e with | Overwrite e -> e |> Some | _ -> None) |> Option.defaultValue false

        match ofn with
        | Some f -> ctx.workerNodeAdmProxy.tryExportPublicKey f o
        | None ->
            Logger.logWarn "exportPublicKey - output folder name was not provided."
            Ok()


    let importPublicKey (ctx : WorkerNodeAdmContext) (x : list<ImportPublicKeyArgs>) =
        let ifn = x |> List.tryPick (fun e -> match e with | InputFileName e -> e |> FileName |> Some | _ -> None)

        match ifn with
        | Some f ->
            match ctx.workerNodeAdmProxy.tryImportPartitionerPublicKey f with
            | Ok (k, key) ->
                let w = k.value |> MessagingClientId |> PartitionerId
                ctx.workerNodeAdmProxy.tryUpdatePartitionerPublicKey w key
            | Error e -> Error e
        | None ->
            Logger.logWarn "importPublicKey - input file name was not provided."
            Ok()
