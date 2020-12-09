namespace Softellect.Sys

open System
open System.IO
open Newtonsoft.Json
open FSharp.Interop.Dynamic

open Softellect.Sys

module AppSettings =

    type ConfigSection =
        | ConfigSection of string

        static member appSettings = ConfigSection "appSettings"
        static member connectionStrings = ConfigSection "connectionStrings"


    type ConfigKey =
        | ConfigKey of string


    type ConfigValueType =
        | StringValue
        | IntValue
        | DecimalValue


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


    let tryGet jsonObj (ConfigSection section) (ConfigKey key) =
        try
            let value = (jsonObj?(section)?(key))
            match box value with
            | null -> Ok None
            | _ -> value.ToString() |> Some |> Ok
        with
        | e -> Error e


    let trySet jsonObj (ConfigSection section) (ConfigKey key) (value : string) =
        try
            jsonObj?(section)?(key) <- value
            Ok()
        with
        | e -> Error e


    type ConfigValue =
        | StringConfigValue of string
        | IntConfigValue of int
        | DecimalConfigValue of decimal

        member cv.jsonValue =
            match cv with
            | StringConfigValue v -> v
            | IntConfigValue v -> $"%i{v}"
            | DecimalConfigValue v -> $"{v}"

        member cv.trySetValue jsonObj section key = trySet jsonObj section key cv.jsonValue

        static member tryGetValue jsonObj valueType section key =
            try
                match valueType, tryGet jsonObj section key with
                | StringValue, Ok v -> v |> Option.bind (fun x -> x |> StringConfigValue |> Some) |> Ok
                | IntValue, Ok v -> v |> Option.bind (fun x -> Int32.Parse x |> IntConfigValue |> Some) |> Ok
                | DecimalValue, Ok v -> v |> Option.bind (fun x -> Decimal.Parse x |> DecimalConfigValue |> Some) |> Ok
                | _, Error e -> Error e
            with
            | e -> Error e


    /// A thin get / set wrapper around appsettings.json or similarly structured JSON file.
    /// Currently it supports only simple key value pairs.
    /// If you need anything more advanced, then you need to parse the output string yourself.
    type AppSettingsProvider private (fileName, jsonObj) =
        member a.tryGetString key = ConfigValue.tryGetValue StringValue jsonObj ConfigSection.appSettings key
        member a.tryGetInt key = ConfigValue.tryGetValue IntValue jsonObj ConfigSection.appSettings key
        member a.tryGetDecimal key = ConfigValue.tryGetValue StringValue jsonObj ConfigSection.appSettings key

        member a.trySetAppSetting key value = trySet jsonObj ConfigSection.appSettings key value

        member a.tryGetConnectionString key = tryGet jsonObj ConfigSection.connectionStrings key
        member a.trySetConnectionString key value = trySet jsonObj ConfigSection.connectionStrings key value
        member a.trySave() = trySaveJson fileName jsonObj
        member a.trySaveAs newFileName = trySaveJson newFileName jsonObj

        static member tryCreate fileName =
            match tryOpenJson fileName with
            | Ok jsonObj -> (fileName, jsonObj) |> AppSettingsProvider |> Ok
            | Error e -> Error e

