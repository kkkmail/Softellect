namespace Softellect.Sys

open System.IO
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.Sys.Retry
open Softellect.Sys.Errors

module FileSystemTypes =
    let private serializationFormat = BinaryZippedFormat
    let private serializationErrFormat = JSonFormat

    let defaultFileStorageFolder = FolderName "C:\\FileStorage"


    type TableName =
        | TableName of string


    let private getFolderName getStorageFolder (ServiceName serviceName) (TableName tableName) =
        let topFolder : FolderName = getStorageFolder()
        let folder = (topFolder.combine (FolderName serviceName)).combine (FolderName tableName)

        try
            Directory.CreateDirectory(folder.value) |> ignore
            Ok folder
        with
        | e -> e |> GetFolderNameExn |> FileErr |> Error


    let private getFileName<'A> getStorageFolder (fmt : SerializationFormat) serviceName tableName (objectId : 'A) =
        try
            match getFolderName getStorageFolder serviceName tableName with
            | Ok folder ->
                let fileName = ((FileName $"{objectId}").addExtension fmt.fileExtension).combine folder
                Ok fileName
            | Error e -> Error e
        with
        | e -> e |> GetFileNameExn |> FileErr |> Error


    /// Tries to load data.
    /// Returns (Ok (Some Object)) if object was found and successfully loaded.
    /// Returns (Ok None) if the object is not found.
    /// Returns (Error e) in case of any other issues.
    let tryLoadData<'T, 'A> getStorageFolder serviceName tableName (objectId : 'A) =
        let w () =
            try
                match getFileName getStorageFolder serializationFormat serviceName tableName objectId with
                | Ok (FileName f) ->
                    let x =
                        if File.Exists f
                        then
                            let data = File.ReadAllBytes f
                            let retVal = data |> deserialize serializationFormat |> Some |> Ok
                            retVal
                        else Ok None
                    x
                | Error e -> Error e
            with
            | e -> e |> ReadFileExn |> FileErr |> Error
        tryRopFun (fun e -> e |> GeneralFileExn |> FileErr) w


    /// Loads the data if successfully loaded and returns an error if an object is not found OR any error occurs.
    let loadData<'T, 'A> getStorageFolder serviceName tableName (objectId : 'A) =
        match tryLoadData<'T, 'A> getStorageFolder serviceName tableName objectId with
        | Ok (Some r) -> Ok r
        | Ok None ->
            match getFileName<'A> getStorageFolder serializationFormat serviceName tableName objectId with
            | Ok f -> f |> FileNotFoundErr |> FileErr |> Error
            | Error e -> Error e
        | Error e -> Error e


    let private saveDataImpl<'T, 'A> getStorageFolder fmt serviceName tableName (objectId : 'A) (t : 'T) =
        let w() =
            try
                match getFileName getStorageFolder fmt serviceName tableName objectId with
                | Ok (FileName f) ->
                    let d = t |> serialize fmt
                    File.WriteAllBytes (f, d)
                    Ok ()
                | Error e -> Error e
            with
            | e -> e |> WriteFileExn |> FileErr |> Error
        tryRopFun (fun e -> e |> GeneralFileExn |> FileErr) w


    let saveData<'T, 'A> getStorageFolder serviceName tableName (objectId : 'A) (t : 'T) =
        saveDataImpl<'T, 'A> getStorageFolder serializationFormat serviceName tableName objectId t


    /// Write-once error objects.
    let saveErrData<'T, 'A> getStorageFolder serviceName tableName (objectId : 'A) (t : 'T) =
        saveDataImpl<'T, 'A> getStorageFolder serializationErrFormat serviceName tableName objectId t

    /// Tries to delete object if it exists.
    let tryDeleteData<'T, 'A> getStorageFolder serviceName tableName (objectId : 'A) =
        let w() =
            match getFileName getStorageFolder serializationFormat serviceName tableName objectId with
            | Ok f ->
                try
                    if File.Exists f.value then File.Delete f.value
                    Ok ()
                with
                | e -> (f, e) |> DeleteFileExn |> FileErr |> Error
            | Error e -> Error e
        tryRopFun (fun e -> e |> GeneralFileExn |> FileErr) w


    let getObjectIds<'A> getStorageFolder serviceName tableName (creator : string -> 'A) =
        let w() =
            try
                match getFolderName getStorageFolder serviceName tableName with
                | Ok (FolderName folder) ->
                    Directory.GetFiles(folder, "*" + serializationFormat.fileExtension.value)
                    |> List.ofArray
                    |> List.map (fun e -> Path.GetFileNameWithoutExtension e)
                    |> List.map creator
                    |> Ok
                | Error e -> Error e
            with
            | e -> e |> GetObjectIdsExn |> FileErr |> Error
        tryRopFun (fun e -> e |> GeneralFileExn |> FileErr) w


    let loadObjects<'T, 'A> getStorageFolder serviceName tableName (creator : string -> 'A) =
        match getObjectIds getStorageFolder serviceName tableName creator with
        | Ok i ->
            i
            |> List.map (loadData<'T, 'A> getStorageFolder serviceName tableName)
            |> Ok
        | Error e -> Error e
