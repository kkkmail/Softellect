namespace Softellect.Sys

open System
open System.Data.SQLite
open FSharp.Data.Sql
open System.Data
open System.Data.SqlClient

open Softellect.Sys.Retry
open Softellect.Sys.AppSettings
open Softellect.Sys.DataAccessErrors
open Softellect.Sys.AppSettings
open Softellect.Sys.Primitives
open Softellect.Sys.VersionInfo
open Softellect.Sys.Errors

module DataAccess =

    [<Literal>]
    let AppConfigFile : string = __SOURCE_DIRECTORY__ + @"\app.config"


    let messagingConnectionStringKey = ConfigKey "MessagingService"


    let getConnectionString fileName connKey defaultValue =
        match AppSettingsProvider.tryCreate fileName with
        | Ok provider ->
            match provider.tryGetConnectionString connKey with
            | Ok (Some EmptyString) -> defaultValue
            | Ok (Some s) -> s
            | _ -> defaultValue
        | _ -> defaultValue
        |> ConnectionString


    [<Literal>]
    let private MessagingDbName = MsgSvcBaseName


    [<Literal>]
    let private MessagingConnectionStringValue = "Server=localhost;Database=" + MessagingDbName + ";Integrated Security=SSPI"


    let private getMessagingConnectionStringImpl() = getConnectionString AppSettingsFile messagingConnectionStringKey MessagingConnectionStringValue
    let private messagingConnectionString = Lazy<ConnectionString>(getMessagingConnectionStringImpl)
    let getMessagingConnectionString() = messagingConnectionString.Value


    [<Literal>]
    let MessagingSqlProviderName : string = "name=MessagingService"


    let buildConnectionString (key : string) : string =
        [
            Some $"Server=localhost;Database=%s{key};Integrated Security=SSPI"
        ]
        |> List.pick (fun x -> x)


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


    let toError g f = f |> g |> DbErr |> Error
    let addError g f e = ((f |> g |> DbErr) + e) |> Error
    let mapException e = e |> DbExn |> DbErr
    let mapExceptionToError e = e |> DbExn |> DbErr |> Error


    /// Maps missing value (None) to DbErr.
    let mapDbError f i v =
        v
        |> Option.map Ok
        |> Option.defaultValue (i |> f |> DbErr |> Error)


    let tryDbFun g =
        let w() =
            try
                g()
            with
            | e -> mapExceptionToError e

        tryRopFun mapException w


    /// Analog of ExecuteScalar - gets the first column of the first result set.
    /// In contrast to ExecuteScalar it also expects it to be castable to int32.
    /// Otherwise it will return None.
    /// This function is monsly used to get the number of updated rows.
    let mapIntScalar (r : Common.SqlEntity[]) =
        r
        |> Array.map(fun e -> e.ColumnValues |> List.ofSeq |> List.head)
        |> Array.map snd
        |> Array.map (fun e -> match e with | :? Int32 as i -> Some i | _ -> None)
        |> Array.tryHead
        |> Option.bind id


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


    let bindIntScalar  f q r = r |> mapIntScalar |> bindOptionError f q
