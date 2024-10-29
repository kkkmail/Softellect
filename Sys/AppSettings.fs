namespace Softellect.Sys
open Newtonsoft.Json.Linq

open System
open System.Collections.Generic
open System.IO
open Newtonsoft.Json
open Argu
open FSharp.Interop.Dynamic
open Softellect.Sys.Core
open Softellect.Sys.Primitives

module AppSettings =

    [<Literal>]
    let ValueSeparator = "="


    [<Literal>]
    let ListSeparator = ";"


    [<Literal>]
    let DiscriminatedUnionSeparator = "|"


    /// Expects a string in the form:
    ///     someField1:SomeValue1;someField2:SomeValue2
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


    let tryOpenJson (FileName fileName) =
        try
            let json = File.ReadAllText(fileName)
            let jsonObj = JsonConvert.DeserializeObject(json)
            Ok jsonObj
        with
        | e -> Error e


    let trySaveJson (FileName fileName) jsonObj =
        try
            let output = JsonConvert.SerializeObject(jsonObj, Formatting.Indented)
            File.WriteAllText(fileName, output)
            Ok()
        with
        | e -> Error e


    let tryGetString (jsonObj : Newtonsoft.Json.Linq.JObject) (ConfigSection section) (ConfigKey key) =
        try
            // Safely try to get the section
            match jsonObj.TryGetValue(section) with
            | true, sectionObj ->
                match sectionObj :?> Newtonsoft.Json.Linq.JObject with
                | null -> Ok None
                | sectionJObj ->
                    // Safely try to get the key in the section
                    match sectionJObj.TryGetValue(key) with
                    | true, value when value <> null -> value.ToString() |> Some |> Ok
                    | _ -> Ok None
            | _ -> Ok None
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


    let tryGetFromJson<'T> jsonObj section key : Result<'T option, exn> =
        match tryGetString jsonObj section key with
        | Ok (Some s) ->
            try
                jsonDeserialize<'T> s |> Some |> Ok
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


    /// Returns a value if parsed properly. Otherwise ignores missing and/or incorrect value
    /// and returns provided default value instead.
    let tryGetFromJsonOrDefault<'T> (defaultValue : 'T) jsonObj section key : 'T =
        match tryGetFromJson<'T> jsonObj section key with
        | Ok (Some v) -> v
        | _ -> defaultValue


    let trySet jsonObj (ConfigSection section) (ConfigKey key) value =
        try
            let sectionObj =
                if jsonObj?(section) = null then
                    let newSection = new JObject()
                    jsonObj?Add(section, newSection)
                    newSection
                else jsonObj?(section)

            sectionObj?(key) <- $"{value}"
            Ok()
        with
        | e -> Error e


    /// A thin (get / set) wrapper around appsettings.json or similarly structured JSON file.
    /// Currently it supports only simple key value pairs.
    /// If you need anything more advanced, then get the string and parse it yourself.
    type AppSettingsProvider private (fileName, jsonObj : Newtonsoft.Json.Linq.JObject) =
        member _.tryGetString key = tryGetString jsonObj ConfigSection.appSettings key
        member _.tryGetInt key = tryGetInt jsonObj ConfigSection.appSettings key
        member _.tryGetDecimal key = tryGetDecimal jsonObj ConfigSection.appSettings key
        member _.tryGetDouble key = tryGetDouble jsonObj ConfigSection.appSettings key
        member _.tryGetGuid key = tryGetGuid jsonObj ConfigSection.appSettings key
        member _.tryGetBool key = tryGetBool jsonObj ConfigSection.appSettings key
        member _.tryGet<'T> tryCreate key = tryGet<'T> tryCreate jsonObj ConfigSection.appSettings key
        member _.tryGetFromJson<'T> key = tryGetFromJson<'T> jsonObj ConfigSection.appSettings key

        member _.tryGetOrDefault<'T> (defaultValue : 'T) tryCreate key =
            tryGetOrDefault<'T> defaultValue tryCreate jsonObj ConfigSection.appSettings key

        member _.tryGetFromJsonOrDefault<'T> (defaultValue : 'T) key =
            tryGetFromJsonOrDefault<'T> defaultValue jsonObj ConfigSection.appSettings key

        member _.trySet key value = trySet jsonObj ConfigSection.appSettings key value

        member _.tryGetConnectionString key = tryGetString jsonObj ConfigSection.connectionStrings key
        member _.trySetConnectionString key value = trySet jsonObj ConfigSection.connectionStrings key value

        member _.trySave() = trySaveJson fileName jsonObj
        member _.trySaveAs newFileName = trySaveJson newFileName jsonObj

        static member tryCreate fileName =
            let fullFileName = getFileName fileName
            match tryOpenJson fullFileName with
            | Ok jsonObj -> (fullFileName, (jsonObj :?> Newtonsoft.Json.Linq.JObject)) |> AppSettingsProvider |> Ok
            | Error e -> Error e


    type AppSettingsProviderResult = Result<AppSettingsProvider, exn>


    /// A simple command line handler to save default settings into appconfig.json file.
    /// Thisis helpful when the structures change and you want to reset the settings.
    [<CliPrefix(CliPrefix.None)>]
    type SettingsArguments =
        | [<Unique>] [<First>] [<AltCommandLine("s")>] Save

    with
        interface IArgParserTemplate with
            member this.Usage =
                match this with
                | Save -> "saves default settings into appconfig.json file."


    type SettingsTask =
        | SaveSettingsTask of (unit -> unit)

        member task.run () =
            match task with
            | SaveSettingsTask s -> s()

        static member private tryCreateSaveSettingsTask s (p : list<SettingsArguments>) : SettingsTask option =
            p |> List.tryPick (fun e -> match e with | Save -> s |> SaveSettingsTask |> Some | _ -> None)

        static member tryCreate s p =
            [
                SettingsTask.tryCreateSaveSettingsTask s
            ]
            |> List.tryPick (fun e -> e p)
