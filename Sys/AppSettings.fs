namespace Softellect.Sys

open System
open System.IO
open Newtonsoft.Json
open FSharp.Interop.Dynamic

module AppSettings =

    type ConfigSection =
        | ConfigSection of string

        static member appSettings = ConfigSection "appSettings"
        static member connectionStrings = ConfigSection "connectionStrings"


    type ConfigKey =
        | ConfigKey of string


    let tryOpenJson fileName =
        try
            let json = File.ReadAllText(fileName)
            let jsonObj = JsonConvert.DeserializeObject(json)
            Ok jsonObj
        with
        | e -> Error e


    let trySaveJson fileName jsonObj =
        try
            let output = JsonConvert.SerializeObject(jsonObj, Formatting.Indented)
            File.WriteAllText(fileName, output)
            Ok()
        with
        | e -> Error e


    let tryGetString jsonObj (ConfigSection section) (ConfigKey key) =
        try
            let value = (jsonObj?(section)?(key))
            match box value with
            | null -> Ok None
            | _ -> value.ToString() |> Some |> Ok
        with
        | e -> Error e


    let tryGetInt jsonObj section key =
        match tryGetString jsonObj section key with
        | Ok (Some s) ->
            try
                Int32.Parse s |> Some |> Ok
            with
            | e -> Error e
        | Ok None -> Ok None

        | Error e -> Error e


    let tryGetDecimal jsonObj section key =
        match tryGetString jsonObj section key with
        | Ok (Some s) ->
            try
                Decimal.Parse s |> Some |> Ok
            with
            | e -> Error e
        | Ok None -> Ok None
        | Error e -> Error e


    let tryGetGuid jsonObj section key =
        match tryGetString jsonObj section key with
        | Ok (Some s) ->
            try
                Guid.Parse s |> Some |> Ok
            with
            | e -> Error e
        | Ok None -> Ok None

        | Error e -> Error e

    let trySet jsonObj (ConfigSection section) (ConfigKey key) value =
        try
            jsonObj?(section)?(key) <- $"{value}"
            Ok()
        with
        | e -> Error e


    /// A thin (get / set) wrapper around appsettings.json or similarly structured JSON file.
    /// Currently it supports only simple key value pairs.
    /// If you need anything more advanced, then get the string and parse it yourself.
    type AppSettingsProvider private (fileName, jsonObj) =
        member a.tryGetString key = tryGetString jsonObj ConfigSection.appSettings key
        member a.tryGetInt key = tryGetInt jsonObj ConfigSection.appSettings key
        member a.tryGetDecimal key = tryGetDecimal jsonObj ConfigSection.appSettings key
        member a.tryGetGuid key = tryGetGuid jsonObj ConfigSection.appSettings key

        member a.trySet key value = trySet jsonObj ConfigSection.appSettings key value

        member a.tryGetConnectionString key = tryGetString jsonObj ConfigSection.connectionStrings key
        member a.trySetConnectionString key value = trySet jsonObj ConfigSection.connectionStrings key value

        member a.trySave() = trySaveJson fileName jsonObj
        member a.trySaveAs newFileName = trySaveJson newFileName jsonObj

        static member tryCreate fileName =
            match tryOpenJson fileName with
            | Ok jsonObj -> (fileName, jsonObj) |> AppSettingsProvider |> Ok
            | Error e -> Error e
