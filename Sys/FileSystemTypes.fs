namespace Softellect.Sys

open System.IO
open Softellect.Sys.Primitives
open Softellect.Sys.Core
open Softellect.Sys.Retry
open Softellect.Sys.Errors

module FileSystemTypes =
    let private serializationFormat = BinaryZippedFormat
    let private serializationErrFormat = JSonFormat

    /// TODO kk:20241015 - Make it configurable.
    /// The folder where the files are stored.
    let private fileStorageFolder = "C:\\FileStorage"


    type TableName =
        | TableName of string


    let private getFolderName serviceName (TableName tableName) =
        let folder = fileStorageFolder + "\\" + serviceName + "\\" + tableName

        try
            Directory.CreateDirectory(folder) |> ignore
            Ok folder
        with
        | e -> e |> GetFolderNameExn |> FileErr |> Error


    let private getFileName<'A> (fmt : SerializationFormat) serviceName tableName (objectId : 'A) =
        try
            match getFolderName serviceName tableName with
            | Ok folder ->
                let file = Path.Combine(folder, objectId.ToString() + "." + fmt.fileExtension)
                Ok file
            | Error e -> Error e
        with
        | e -> e |> GetFileNameExn |> FileErr |> Error


    /// Tries to load data.
    /// Returns (Ok (Some Object)) if object was found and successfully loaded.
    /// Returns (Ok None) if the object is not found.
    /// Returns (Error e) in case of any other issues.
    let tryLoadData<'T, 'A> serviceName tableName (objectId : 'A) =
        let w () =
            try
                match getFileName serializationFormat serviceName tableName objectId with
                | Ok f ->
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
    let loadData<'T, 'A> serviceName tableName (objectId : 'A) =
        match tryLoadData<'T, 'A> serviceName tableName objectId with
        | Ok (Some r) -> Ok r
        | Ok None ->
            match getFileName<'A> serializationFormat serviceName tableName objectId with
            | Ok f -> f |> FileNotFoundErr |> FileErr |> Error
            | Error e -> Error e
        | Error e -> Error e


    let private saveDataImpl<'T, 'A> fmt serviceName tableName (objectId : 'A) (t : 'T) =
        let w() =
            try
                match getFileName fmt serviceName tableName objectId with
                | Ok f ->
                    let d = t |> serialize fmt
                    File.WriteAllBytes (f, d)
                    Ok ()
                | Error e -> Error e
            with
            | e -> e |> WriteFileExn |> FileErr |> Error
        tryRopFun (fun e -> e |> GeneralFileExn |> FileErr) w


    let saveData<'T, 'A> serviceName tableName (objectId : 'A) (t : 'T) =
        saveDataImpl<'T, 'A> serializationFormat serviceName tableName objectId t


    /// Write-once error objects.
    let saveErrData<'T, 'A> serviceName tableName (objectId : 'A) (t : 'T) =
        saveDataImpl<'T, 'A> serializationErrFormat serviceName tableName objectId t

    /// Tries to delete object if it exists.
    let tryDeleteData<'T, 'A> serviceName tableName (objectId : 'A) =
        let w() =
            try
                match getFileName serializationFormat serviceName tableName objectId with
                | Ok f ->
                    if File.Exists f then File.Delete f
                    Ok ()
                | Error e -> Error e
            with
            | e -> e |> DeleteFileExn |> FileErr |> Error
        tryRopFun (fun e -> e |> GeneralFileExn |> FileErr) w


    let getObjectIds<'A> serviceName tableName (creator : string -> 'A) =
        let w() =
            try
                match getFolderName serviceName tableName with
                | Ok folder ->
                    Directory.GetFiles(folder, "*." + serializationFormat.fileExtension)
                    |> List.ofArray
                    |> List.map (fun e -> Path.GetFileNameWithoutExtension e)
                    |> List.map creator
                    |> Ok
                | Error e -> Error e
            with
            | e -> e |> GetObjectIdsExn |> FileErr |> Error
        tryRopFun (fun e -> e |> GeneralFileExn |> FileErr) w


    let loadObjects<'T, 'A> serviceName tableName (creator : string -> 'A) =
        match getObjectIds serviceName tableName creator with
        | Ok i ->
            i
            |> List.map (loadData<'T, 'A> serviceName tableName)
            |> Ok
        | Error e -> Error e
