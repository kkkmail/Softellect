namespace DbData

open System
open System.Data.SQLite
open FSharp.Data.Sql
open System.Data
open System.Data.SqlClient

open Softellect.Sys.Retry
open Softellect.Sys.AppSettings

open Primitives.VersionInfo
open ClmSys.GeneralPrimitives
open ClmSys.GeneralErrors
open ClmSys.ClmErrors
open Primitives.GeneralData
open Primitives.GeneralData

module Configuration =

    [<Literal>]
    let ContGenDbName = ContGenBaseName


    [<Literal>]
    let ContGenConnectionStringValue = "Server=localhost;Database=" + ContGenDbName + ";Integrated Security=SSPI"


    let contGenConnectionStringKey = ConfigKey "ContGenService"
    let workerNodeConnectionStringKey = ConfigKey "WorkerNodeService"


    let private getConnectionString fileName connKey defaultValue =
        match AppSettingsProvider.tryCreate fileName with
        | Ok provider ->
            match provider.tryGetConnectionString connKey with
            | Ok (Some EmptyString) -> defaultValue
            | Ok (Some s) -> s
            | _ -> defaultValue
        | _ -> defaultValue
        |> ConnectionString


    let private getContGenConnectionStringImpl() = getConnectionString appSettingsFile contGenConnectionStringKey ContGenConnectionStringValue


    let private contGenConnectionString = Lazy<ConnectionString>(getContGenConnectionStringImpl)
    let getContGenConnectionString() = contGenConnectionString.Value


    [<Literal>]
    let ClmCommandTimeout = 7200


    [<Literal>]
    let ContGenSqlProviderName : string = "name=ContGenService"


    [<Literal>]
    let WorkerNodeDbName = WorkerNodeSvcBaseName


    [<Literal>]
    let WorkerNodeConnectionStringValue = "Server=localhost;Database=" + WorkerNodeDbName + ";Integrated Security=SSPI"


    let private getWorkerNodeConnectionStringImpl() = getConnectionString appSettingsFile workerNodeConnectionStringKey WorkerNodeConnectionStringValue
    let private workerNodeConnectionString = Lazy<ConnectionString>(getWorkerNodeConnectionStringImpl)
    let getWorkerNodeSvcConnectionString() = workerNodeConnectionString.Value


    [<Literal>]
    let WorkerNodeSqlProviderName : string = "name=WorkerNodeService"
