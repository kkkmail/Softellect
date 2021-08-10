namespace Softellect.Sys

open System
open System.IO
open Newtonsoft.Json
open FSharp.Interop.Dynamic

module AppSettings =

    [<Literal>]
    let ValueSeparator = ":"


    [<Literal>]
    let ListSeparator = ","


    [<Literal>]
    let DiscriminatedUnionSeparator = "|"


    /// Expects a string in the form:
    ///     someField1:SomeValue1,someField2:SomeValue2
    let parseSimpleSetting (s : string) =
        let p =
            s.Split ListSeparator
            |> List.ofArray
            |> List.map (fun e -> e.Split ValueSeparator)
            |> List.map (fun e -> e |> Array.map (fun a -> a.Trim()))
            |> List.map (fun e -> if e.Length = 2 then Some (e.[0], e.[1]) else None)
            |> List.choose id
            |> Map.ofList

        p


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


    let tryGetDouble jsonObj section key =
        match tryGetString jsonObj section key with
        | Ok (Some s) ->
            try
                Double.Parse s |> Some |> Ok
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


    let tryGetBool jsonObj section key =
        match tryGetString jsonObj section key with
        | Ok (Some s) ->
            try
                bool.Parse s |> Some |> Ok
            with
            | e -> Error e
        | Ok None -> Ok None
        | Error e -> Error e


    let tryGet<'T> tryCreate jsonObj section key : Result<'T option, exn> =
        match tryGetString jsonObj section key with
        | Ok (Some s) ->
            try
                match tryCreate s with
                | Ok v -> Ok (Some v)
                | Error e -> e |> InvalidDataException :> exn |> Error
            with
            | e -> Error e
        | Ok None -> Ok None
        | Error e -> Error e


    /// Returns a value if parsed properly. Otherwise ignores missing and/or incorrect value
    /// and returns provided default value instead.
    let tryGetOrDefault<'T> (defaultValue : 'T) tryCreate jsonObj section key : 'T =
        match tryGet<'T> tryCreate jsonObj section key with
        | Ok (Some v) -> v
        | _ -> defaultValue


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
        member _.tryGetString key = tryGetString jsonObj ConfigSection.appSettings key
        member _.tryGetInt key = tryGetInt jsonObj ConfigSection.appSettings key
        member _.tryGetDecimal key = tryGetDecimal jsonObj ConfigSection.appSettings key
        member _.tryGetDouble key = tryGetDouble jsonObj ConfigSection.appSettings key
        member _.tryGetGuid key = tryGetGuid jsonObj ConfigSection.appSettings key
        member _.tryGetBool key = tryGetBool jsonObj ConfigSection.appSettings key
        member _.tryGet<'T> tryCreate key = tryGet<'T> tryCreate jsonObj ConfigSection.appSettings key

        member _.tryGetOrDefault<'T> (defaultValue : 'T) tryCreate key =
            tryGetOrDefault<'T> defaultValue tryCreate jsonObj ConfigSection.appSettings key

        member _.trySet key value = trySet jsonObj ConfigSection.appSettings key value

        member _.tryGetConnectionString key = tryGetString jsonObj ConfigSection.connectionStrings key
        member _.trySetConnectionString key value = trySet jsonObj ConfigSection.connectionStrings key value

        member _.trySave() = trySaveJson fileName jsonObj
        member _.trySaveAs newFileName = trySaveJson newFileName jsonObj

        static member tryCreate fileName =
            match tryOpenJson fileName with
            | Ok jsonObj -> (fileName, jsonObj) |> AppSettingsProvider |> Ok
            | Error e -> Error e
