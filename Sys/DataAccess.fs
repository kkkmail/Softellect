namespace Softellect.Sys

open System
open System.Data.SQLite
open System.IO
open FSharp.Data.Sql
open System.Data
open System.Data.SqlClient

open Softellect.Sys.Retry
open Softellect.Sys.AppSettings
open Softellect.Sys.Errors
open Softellect.Sys.Primitives
open Softellect.Sys.Core

module DataAccess =

    [<Literal>]
    let AppConfigFile : string = __SOURCE_DIRECTORY__ + @"\app.config"


    let getConnectionString fileName connKey defaultValue =
        let fullPath = getFileName fileName

        printfn $"getConnectionString: fileName = '%A{fileName}', fullPath = '%A{fullPath}', connKey = '%A{connKey}'."

        let r =
            match AppSettingsProvider.tryCreate fullPath with
            | Ok provider ->
                match provider.tryGetConnectionString connKey with
                | Ok (Some EmptyString) ->
                    printfn $"getConnectionString: EmptyString."
                    defaultValue
                | Ok (Some s) ->
                    printfn $"getConnectionString: s = '%A{s}'."
                    s
                | _ ->
                    printfn $"getConnectionString: no data, defaultValue = '%A{defaultValue}'."
                    defaultValue
            | _ ->
                printfn $"getConnectionString: no provider, defaultValue = '%A{defaultValue}'."
                defaultValue
            |> ConnectionString

        printfn $"getConnectionString: r = %A{r}."
        r


    let buildConnectionString (key : string) : string =
        [
            Some $"Server=localhost;Database=%s{key};Integrated Security=SSPI;TrustServerCertificate=yes;"
        ]
        |> List.pick id


    let openConnIfClosed (conn : SqlConnection) =
        match conn.State with
        | ConnectionState.Closed -> do conn.Open()
        | _ -> ()


    let getOpenConn (c : unit -> ConnectionString) =
        let conn = new SqlConnection(c().value)
        openConnIfClosed conn
        conn


    let getOpenSqliteConn (SqliteConnectionString connectionString) =
        let conn = new SQLiteConnection(connectionString)
        conn.Open()
        conn


    let toError g f = f |> g |> Error
    //let addError g f e = ((f |> g |> DbErr) + e) |> Error
    let mapException f e = e |> DbExn |> f
    let mapExceptionToError f e = e |> DbExn |> f |> Error


    let tryDbFun f g =
        let w() =
            try
                g()
            with
            | e ->
                printfn $"tryDbFun: e = %A{e}."
                mapExceptionToError f e

        tryRopFun (mapException f) w


    /// Analog of ExecuteScalar - gets the first column of the first result set.
    /// In contrast to ExecuteScalar it also expects it to be castable to int32.
    /// Otherwise, it will return None.
    /// This function is mostly used to get the number of updated rows.
    let mapIntScalar (r : Common.SqlEntity[]) =
        r
        |> Array.map(fun e -> printfn $"mapIntScalar: '%A{e.ColumnValues}'.")
        |> ignore

        let result =
            r
            |> Array.map(fun e -> e.ColumnValues |> List.ofSeq |> List.head)
            |> Array.map snd
            |> Array.map (fun e -> match e with | :? Int32 as i -> Some i | _ -> None)
            |> Array.tryHead
            |> Option.bind id

        printfn $"mapIntScalar: result = %A{result}."
        result


    /// Binds an unsuccessful database update operation to a given continuation function.
    let bindError f q r =
        match r = 1 with
        | true -> Ok ()
        | false -> toError f q


    /// Binds an unsuccessful database update operation to a given continuation function.
    let bindOptionError f q r =
        match r = (Some 1) with
        | true -> Ok ()
        | false -> toError f q


    /// Binds an unsuccessfull single row update operation to an error continuation function.
    let bindIntScalar f q r = r |> mapIntScalar |> bindOptionError f q
