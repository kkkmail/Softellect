namespace Softellect.Sys
open Microsoft.Identity.Client
open Newtonsoft.Json.Linq

open System
open System.Collections.Generic
open System.IO
open Newtonsoft.Json
open Argu
open FSharp.Interop.Dynamic
open Softellect.Sys.Core
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.Sys.Logging

module AppSettings =

    [<Literal>]
    let SetOnMissing = true


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
        static member logging = ConfigSection "Logging"


    type ConfigKey =
        | ConfigKey of string

        static member projectName = ConfigKey "ProjectName"
        member this.value = let (ConfigKey v) = this in v


    let private trySet (jsonObj: JObject) (ConfigSection section) (ConfigKey key) value =
        try
            // 1) Guard against null jsonObj
            if isNull (box jsonObj) then invalidArg "jsonObj" "jsonObj is null"

            // 2) Get or create the section object
            let sectionObj =
                match jsonObj.[section] with
                | null ->
                    let newSection = JObject()
                    jsonObj.[section] <- newSection
                    newSection
                | token ->
                    token :?> JObject

            // 3) Set the key value
            sectionObj.[key] <- JValue($"{value}")

            Ok()
        with
        | e -> Error e


    let private trySetNested (jsonObj: JObject) (sections: string list) (key: string) value =
        try
            if isNull (box jsonObj) then invalidArg "jsonObj" "jsonObj is null"

            let rec getOrCreateSection (currentObj: JObject) (sectionPath: string list) =
                match sectionPath with
                | [] -> currentObj
                | section :: rest ->
                    let nextObj =
                        match currentObj.[section] with
                        | null ->
                            let newObj = JObject()
                            currentObj.[section] <- newObj
                            newObj
                        | token -> token :?> JObject
                    getOrCreateSection nextObj rest

            let targetObj = getOrCreateSection jsonObj sections
            targetObj.[key] <- JValue($"{value}")
            Ok()
        with
        | e -> Error e


    let private tryGetNested (jsonObj: JObject) (sections: string list) (key: string) =
        try
            let rec navigate (currentObj: JObject) (sectionPath: string list) =
                match sectionPath with
                | [] -> Some currentObj
                | section :: rest ->
                    match currentObj.TryGetValue(section) with
                    | true, token ->
                        match token with
                        | :? JObject as nextObj -> navigate nextObj rest
                        | _ -> None
                    | _ -> None

            match navigate jsonObj sections with
            | Some targetObj ->
                match targetObj.TryGetValue(key) with
                | true, value when value <> null -> Ok (Some (value.ToString()))
                | _ -> Ok None
            | None -> Ok None
        with
        | e -> Error e


    let private tryGetNestedSection (jsonObj: JObject) (sections: string list) =
        try
            let rec navigate (currentObj: JObject) (sectionPath: string list) =
                match sectionPath with
                | [] -> Some currentObj
                | section :: rest ->
                    match currentObj.TryGetValue(section) with
                    | true, token ->
                        match token with
                        | :? JObject as nextObj -> navigate nextObj rest
                        | _ -> None
                    | _ -> None

            Ok (navigate jsonObj sections)
        with
        | e -> Error e


    let private trySetArray<'T> (jsonObj: JObject) (ConfigSection section) (ConfigKey key) (items: 'T list) =
        try
            // 1) Guard against null jsonObj
            if isNull (box jsonObj) then invalidArg "jsonObj" "jsonObj is null"

            // 2) Get or create the section object
            let sectionObj =
                match jsonObj.[section] with
                | null ->
                    let newSection = JObject()
                    jsonObj.[section] <- newSection
                    newSection
                | token ->
                    token :?> JObject

            // 3) Serialize items to JSON array
            let jsonArray = JArray()
            for item in items do
                let itemJson = JsonConvert.SerializeObject(item)
                let itemObj = JObject.Parse(itemJson)
                jsonArray.Add(itemObj)

            // 4) Set the key to the array
            sectionObj.[key] <- jsonArray

            Ok()
        with
        | e -> Error e


    let private tryGetArray<'T> (jsonObj: JObject) (ConfigSection section) (ConfigKey key) : Result<'T list option, exn> =
        try
            match jsonObj.TryGetValue(section) with
            | true, sectionObj ->
                match sectionObj :?> JObject with
                | null -> Ok None
                | sectionJObj ->
                    match sectionJObj.TryGetValue(key) with
                    | true, value when value <> null ->
                        match value :?> JArray with
                        | null -> Ok None
                        | arr ->
                            let items =
                                arr
                                |> Seq.map (fun token -> token.ToString() |> jsonDeserialize<'T>)
                                |> Seq.toList
                            Ok (Some items)
                    | _ -> Ok None
            | _ -> Ok None
        with
        | e -> Error e


    let private tryOpenJson (FileName fileName) =
        try
            let json = File.ReadAllText(fileName)
            let jsonObj = JsonConvert.DeserializeObject(json)
            Ok jsonObj
        with
        | e -> Error e


    let private trySaveJson (FileName fileName) jsonObj =
        try
            let output = JsonConvert.SerializeObject(jsonObj, Formatting.Indented)
            File.WriteAllText(fileName, output)
            Ok()
        with
        | e -> Error e


    let private tryGetString (jsonObj : Newtonsoft.Json.Linq.JObject) (ConfigSection section) (ConfigKey key) =
        try
            // Safely try to get the section.
            match jsonObj.TryGetValue(section) with
            | true, sectionObj ->
                match sectionObj :?> Newtonsoft.Json.Linq.JObject with
                | null -> Ok None
                | sectionJObj ->
                    // Safely try to get the key in the section.
                    match sectionJObj.TryGetValue(key) with
                    | true, value when value <> null -> value.ToString() |> Some |> Ok
                    | _ -> Ok None
            | _ -> Ok None
        with
        | e -> Error e


    let private tryGetNestedString (jsonObj: JObject) (sections: string list) =
        try
            let rec getValue (currentObj: JObject) (keys: string list) =
                match keys with
                | [] -> Ok None // No more keys to traverse
                | [lastKey] -> // Final key to retrieve the value
                    match currentObj.TryGetValue(lastKey) with
                    | true, value when value <> null -> Ok(Some(value.ToString()))
                    | _ -> Ok None
                | key :: rest -> // Traverse deeper into the JSON object
                    match currentObj.TryGetValue(key) with
                    | true, value ->
                        match value with
                        | :? JObject as nestedObj -> getValue nestedObj rest
                        | _ -> Ok None
                    | _ -> Ok None
            getValue jsonObj sections
        with
        | e -> Error e


    let private getStringOrDefault setOnMissing defaultValue jsonObj section key =
        match tryGetString jsonObj section key with
        | Ok (Some v) -> v
        | _ ->
            match setOnMissing with
            | true -> trySet jsonObj section key defaultValue |> ignore
            | false -> ()

            defaultValue


    let private tryGet<'T> tryCreate jsonObj section key : Result<'T option, exn> =
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


    let private tryGetFromJson<'T> jsonObj section key : Result<'T option, exn> =
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
    let private getOrDefault<'T> setOnMissing (defaultValue : 'T) tryCreate jsonObj section key : 'T =
        match tryGet<'T> tryCreate jsonObj section key with
        | Ok (Some v) -> v
        | _ ->
            match setOnMissing with
            | true -> trySet jsonObj section key defaultValue |> ignore
            | false -> ()

            defaultValue


    /// Returns a value if parsed properly. Otherwise, ignores missing and/or incorrect value
    /// and returns the provided default value instead.
    let private getFromJsonOrDefault<'T> setOnMissing (defaultValue : 'T) jsonObj section key : 'T =
        match tryGetFromJson<'T> jsonObj section key with
        | Ok (Some v) -> v
        | _ ->
            match setOnMissing with
            | true -> trySet jsonObj section key defaultValue |> ignore
            | false -> ()

            defaultValue


    let private tryCreateInt (s : string) =
        try
            Int32.Parse s |> Ok
        with
        | e -> Error $"{e}"


    let private tryCreateDecimal (s : string) =
        try
            Decimal.Parse s |> Ok
        with
        | e -> Error $"{e}"


    let private tryCreateDouble (s : string) =
        try
            Double.Parse s |> Ok
        with
        | e -> Error $"{e}"


    let private tryCreateGuid (s : string) =
        try
            Guid.Parse s |> Ok
        with
        | e -> Error $"{e}"


    let private tryCreateBool (s : string) =
        try
            Boolean.Parse s |> Ok
        with
        | e -> Error $"{e}"


    let private tryGetInt jsonObj section key = tryGet<int> tryCreateInt jsonObj section key
    let private tryGetDecimal jsonObj section key = tryGet<decimal> tryCreateDecimal jsonObj section key
    let private tryGetDouble jsonObj section key = tryGet<double> tryCreateDouble jsonObj section key
    let private tryGetGuid jsonObj section key = tryGet<Guid> tryCreateGuid jsonObj section key
    let private tryGetBool jsonObj section key = tryGet<bool> tryCreateBool jsonObj section key
    let private tryGetFolderName jsonObj section key = tryGet<FolderName> FolderName.tryCreate jsonObj section key
    let private tryGetFileName jsonObj section key = tryGet<FileName> FileName.tryCreate jsonObj section key


    let private getIntOrDefault setOnMissing defaultValue jsonObj section key =
        getOrDefault<int> setOnMissing defaultValue tryCreateInt jsonObj section key


    let private getDecimalOrDefault setOnMissing defaultValue jsonObj section key =
        getOrDefault<decimal> setOnMissing defaultValue tryCreateDecimal jsonObj section key


    let private getDoubleOrDefault setOnMissing defaultValue jsonObj section key =
        getOrDefault<double> setOnMissing defaultValue tryCreateDouble jsonObj section key


    let private getGuidOrDefault setOnMissing defaultValue jsonObj section key =
        getOrDefault<Guid> setOnMissing defaultValue tryCreateGuid jsonObj section key


    let private getBoolOrDefault setOnMissing defaultValue jsonObj section key =
        getOrDefault<bool> setOnMissing defaultValue tryCreateBool jsonObj section key


    let private getFolderNameOrDefault setOnMissing defaultValue jsonObj section key =
        getOrDefault<FolderName> setOnMissing defaultValue FolderName.tryCreate jsonObj section key


    let private getFileNameOrDefault setOnMissing defaultValue jsonObj section key =
        getOrDefault<FileName> setOnMissing defaultValue FileName.tryCreate jsonObj section key


    /// A thin (get / set) wrapper around appsettings.json or similarly structured JSON file.
    /// Currently, it supports only simple key value pairs.
    /// If you need anything more advanced, then get the string and parse it yourself.
    type AppSettingsProvider private (fileName, jsonObj : Newtonsoft.Json.Linq.JObject, setOnMissing : bool) =
        member _.getStringOrDefault key defaultValue = getStringOrDefault setOnMissing defaultValue jsonObj ConfigSection.appSettings key
        member _.getIntOrDefault key defaultValue = getIntOrDefault setOnMissing defaultValue jsonObj ConfigSection.appSettings key
        member _.getDecimalOrDefault key defaultValue = getDecimalOrDefault setOnMissing defaultValue jsonObj ConfigSection.appSettings key
        member _.getDoubleOrDefault key defaultValue = getDoubleOrDefault setOnMissing defaultValue jsonObj ConfigSection.appSettings key
        member _.getGuidOrDefault key defaultValue = getGuidOrDefault setOnMissing defaultValue jsonObj ConfigSection.appSettings key
        member _.getBoolOrDefault key defaultValue = getBoolOrDefault setOnMissing defaultValue jsonObj ConfigSection.appSettings key
        member _.getFolderNameOrDefault key defaultValue = getFolderNameOrDefault setOnMissing defaultValue jsonObj ConfigSection.appSettings key
        member _.getFileNameOrDefault key defaultValue = getFileNameOrDefault setOnMissing defaultValue jsonObj ConfigSection.appSettings key

        member _.tryGet<'T> tryCreate key = tryGet<'T> tryCreate jsonObj ConfigSection.appSettings key
        member _.tryGetFromJson<'T> key = tryGetFromJson<'T> jsonObj ConfigSection.appSettings key

        member _.getOrDefault<'T> (defaultValue : 'T) tryCreate key =
            getOrDefault<'T> setOnMissing defaultValue tryCreate jsonObj ConfigSection.appSettings key

        member _.getFromJsonOrDefault<'T> (defaultValue : 'T) key =
            getFromJsonOrDefault<'T> setOnMissing defaultValue jsonObj ConfigSection.appSettings key

        member _.trySet key value = trySet jsonObj ConfigSection.appSettings key value
        // member _.tryGetArray<'T> key = tryGetArray<'T> jsonObj ConfigSection.appSettings key
        // member _.trySetArray<'T> key items = trySetArray<'T> jsonObj ConfigSection.appSettings key items
        // member _.trySetNested sections key value = trySetNested jsonObj sections key value
        // member _.tryGetNested sections key = tryGetNested jsonObj sections key
        // member _.tryGetNestedSection sections = tryGetNestedSection jsonObj sections

        member _.tryGetConnectionString key = tryGetString jsonObj ConfigSection.connectionStrings key
        member _.trySetConnectionString key value = trySet jsonObj ConfigSection.connectionStrings key value

        member _.trySave() =
            Logger.logTrace (fun () -> $"AppSettingsProvider.trySave - fileName: '%A{fileName}'.")
            trySaveJson fileName jsonObj

        member _.trySaveAs newFileName = trySaveJson newFileName jsonObj
        member _.setOnMissing = setOnMissing

        member _.getLogLevel() =
            match tryGetNestedString jsonObj ["Logging"; "LogLevel"; "Default"] with
            | Ok(Some value) ->
                match LogLevel.tryDeserialize value with
                | Some v -> v
                | None ->
                    printfn $"Invalid log level value: '{value}'."
                    LogLevel.defaultValue
            | Ok None ->
                printfn "Logging key not found"
                LogLevel.defaultValue
            | Error e ->
                printfn $"ERROR: %A{e}"
                LogLevel.defaultValue

        member a.getProjectName() = a.getStringOrDefault ConfigKey.projectName ProjectName.defaultValue.value |> ProjectName

        member a.save() =
            match a.trySave() with
            | Ok () -> ()
            | Error e -> Logger.logError $"AppSettingsProvider.save - ERROR: '%A{e}'."

        static member tryCreate (fileName : FileName) =
            match fileName.tryGetFullFileName() with
            | Ok fullFileName ->
                match tryOpenJson fullFileName with
                | Ok jsonObj ->
                    let jObj = jsonObj :?> Newtonsoft.Json.Linq.JObject
                    if isNull jObj then fullFileName |> JsonObjectIsNull |> Error
                    else (fullFileName, jObj, SetOnMissing) |> AppSettingsProvider |> Ok
                | Error e -> e |> TryOpenJsonExn |> Error
            | Error e -> Error e

        static member tryCreate () = AppSettingsProvider.tryCreate appSettingsFile


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


    let setLogLevel() =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let logLevel = provider.getLogLevel()
            Logger.logInfo $"setLogLevel: Setting log level to '%A{logLevel}'."
            Logger.setMinLogLevel logLevel
        | Error e -> Logger.logError $"setLogLevel: ERROR - '%A{e}'."


    let getProjectName() =
        match AppSettingsProvider.tryCreate() with
        | Ok provider -> provider.getProjectName()
        | Error e ->
            Logger.logError $"setLogLevel: ERROR - '%A{e}'."
            ProjectName.defaultValue
