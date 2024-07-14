#nowarn "1104"

namespace Softellect.Messaging

open System
open System.Data.SQLite
open FSharp.Data.Sql
open System.Data
open System.Data.SqlClient

open Softellect.Sys.AppSettings
open Softellect.Sys.Primitives
open Softellect.Messaging.VersionInfo
open Softellect.Sys.Core
open Softellect.Sys.DataAccess
open Softellect.Messaging.Errors
open Softellect.Messaging.Primitives

module DataAccess =

    let messagingConnectionStringKey = ConfigKey "MessagingService"


    [<Literal>]
    let private MessagingDbName = MsgSvcBaseName


    [<Literal>]
    let private MessagingConnectionStringValue = "Server=localhost;Database=" + MessagingDbName + ";Integrated Security=SSPI;TrustServerCertificate=yes;"


    let private getMessagingConnectionStringImpl() = getConnectionString AppSettingsFile messagingConnectionStringKey MessagingConnectionStringValue
    let private messagingConnectionString = Lazy<ConnectionString>(getMessagingConnectionStringImpl)
    let getMessagingConnectionString() = messagingConnectionString.Value


    [<Literal>]
    let MessagingSqlProviderName : string = "name=MessagingService"

    [<Literal>]
    let private SqliteStorageFolder = DefaultRootFolder


    [<Literal>]
    let MsgSqliteConnStr = "Data Source=" + __SOURCE_DIRECTORY__ + @"\" + MsgDatabase + @";Version=3;foreign keys=true"


    let getSqlLiteConnStr msgDbLocation = @"Data Source=" + msgDbLocation + ";Version=3;foreign keys=true"
    let msgSqliteConnStr = MsgSqliteConnStr |> SqliteConnectionString


    type Guid
        with
        member g.ToSqliteString() = g.ToString("N")


    //type sqLite = SqlDataProvider<
    //               Common.DatabaseProviderTypes.SQLITE,
    //               SQLiteLibrary = Common.SQLiteLibrary.SystemDataSQLite,
    //               ConnectionString = MsgSqliteConnStr,
    //               //ResolutionPath = resolutionPath,
    //               CaseSensitivityChange = Common.CaseSensitivityChange.ORIGINAL>


    let serializationFormat = BinaryZippedFormat


    type private MsgSvcDb = SqlDataProvider<
                    Common.DatabaseProviderTypes.MSSQLSERVER,
                    ConnectionString = MessagingConnectionStringValue,
                    UseOptionTypes = Common.NullableColumnType.OPTION>


    type private MsgSvcContext = MsgSvcDb.dataContext
    type private MessageEntity = MsgSvcContext.``dbo.MessageEntity``


    let private getDbContext (c : unit -> ConnectionString) = c().value |> MsgSvcDb.GetDataContext


    let private tryCreateMessageImpl (r : MessageEntity) =
        let elevate e = e |> TryCreateMessageErr
        let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryCreateMessageDbErr |> elevate

        let g() =
            let messageId = r.MessageId |> MessageId

            match MessageDeliveryType.tryCreate r.DeliveryTypeId, messagingDataVersion.value = r.DataVersion with
            | Some t, true ->
                {
                    messageDataInfo =
                        {
                            messageId = messageId
                            dataVersion = r.DataVersion |> MessagingDataVersion
                            sender = r.SenderId |> MessagingClientId

                            recipientInfo =
                                {
                                    recipient = r.RecipientId |> MessagingClientId
                                    deliveryType = t
                                }

                            createdOn = r.CreatedOn
                        }

                    messageData = r.MessageData |> deserialize serializationFormat
                }
                |> Some
                |> Ok
            | Some _, false -> InvalidDataVersionErr (messageId, { localVersion = messagingDataVersion; remoteVersion = MessagingDataVersion r.DataVersion }) |> toError
            | None, true -> InvalidDeliveryTypeErr (messageId, r.DeliveryTypeId) |> toError
            | None, false -> InvalidDeliveryTypeAndDataVersionErr (messageId, r.DeliveryTypeId, { localVersion = messagingDataVersion; remoteVersion = MessagingDataVersion r.DataVersion }) |> toError

        tryDbFun fromDbError g


    let tryCreateMessage (t : MessageEntity option) =
        match t with
        | Some v -> v |> tryCreateMessageImpl
        | None -> Ok None


    let tryPickIncomingMessage c (MessagingClientId i) =
        let fromDbError e = e |> TryPickIncomingMessageDbErr |> TryPickMessageErr

        let g () =
            let ctx = getDbContext c

            let x =
                query {
                    for m in ctx.Dbo.Message do
                    where (m.RecipientId = i && m.DataVersion = messagingDataVersion.value)
                    sortBy m.MessageOrder
                    select (Some m)
                    headOrDefault
                }

            tryCreateMessage x

        tryDbFun fromDbError g


    let tryPickOutgoingMessage c (MessagingClientId i) =
        let fromDbError e = e |> TryPickOutgoingMessageDbErr |> TryPickMessageErr

        let g () =
            let ctx = getDbContext c

            let x =
                query {
                    for m in ctx.Dbo.Message do
                    where (m.SenderId = i && m.DataVersion = messagingDataVersion.value)
                    sortBy m.MessageOrder
                    select (Some m)
                    headOrDefault
                }

            tryCreateMessage x

        tryDbFun fromDbError g


    /// We consider the messages are write once, so if the message is already in the database, then we just ignore it.
    ///
    /// Using "with (holdlock)" seems to be causing some deadlocks.
    ///                merge Message with (holdlock) as target
    ///                using (select @messageId, @senderId, @recipientId, @dataVersion, @deliveryTypeId, @messageData, @createdOn)
    ///                as source (messageId, senderId, recipientId, dataVersion, deliveryTypeId, messageData, createdOn)
    ///                on (target.messageId = source.messageId)
    ///                when not matched then
    ///                    insert (messageId, senderId, recipientId, dataVersion, deliveryTypeId, messageData, createdOn)
    ///                    values (source.messageId, source.senderId, source.recipientId, source.dataVersion, source.deliveryTypeId, source.messageData, source.createdOn)
    ///                when matched then
    ///                    update set senderId = source.senderId, recipientId = source.recipientId, dataVersion = source.dataVersion, deliveryTypeId = source.deliveryTypeId, messageData = source.messageData, createdOn = source.createdOn;
    let saveMessage c (m : Message<'D>) =
        let elevate e = e |> SaveMessageErr
        let toError e = e |> CannotSaveMessageErr |> elevate
        let fromDbError e = e |> SaveMessageDbErr |> elevate

        let g() =
            let ctx = getDbContext c

            let r = ctx.Procedures.SaveMessage.Invoke(
                            ``@messageId`` = m.messageDataInfo.messageId.value,
                            ``@senderId`` = m.messageDataInfo.sender.value,
                            ``@recipientId`` = m.messageDataInfo.recipientInfo.recipient.value,
                            ``@dataVersion`` = messagingDataVersion.value,
                            ``@deliveryTypeId`` = m.messageDataInfo.recipientInfo.deliveryType.value,
                            ``@messageData`` = (m.messageData |> serialize serializationFormat))

            r.ResultSet |> bindIntScalar toError m.messageDataInfo.messageId

        tryDbFun fromDbError g


    let deleteMessage c (messageId : MessageId) =
        let elevate e = e |> DeleteMessageErr
        let toError e = e |> CannotDeleteMessageErr |> elevate
        let fromDbError e = e |> DeleteMessageDbErr |> elevate

        let g() =
            let ctx = getDbContext c
            let r = ctx.Procedures.DeleteMessage .Invoke(``@messageId`` = messageId.value)
            r.ResultSet |> bindIntScalar toError messageId

        tryDbFun fromDbError g


    let deleteExpiredMessages c (expirationTime : TimeSpan) =
        let elevate e = e |> DeleteExpiredMessagesErr
        let fromDbError e = e |> DeleteExpiredMessagesDbErr |> elevate

        let g() =
            let ctx = getDbContext c
            let r = ctx.Procedures.DeleteExpiredMessages.Invoke(``@dataVersion`` = messagingDataVersion.value, ``@createdOn`` = DateTime.Now - expirationTime)
            r.ResultSet |> ignore
            Ok()

        tryDbFun fromDbError g


    //let private executeSqlite (connection : #DbConnection) (sql : string) (parameters : _) =
    //    let g() =
    //        let result = connection.Execute(sql, parameters)
    //        Ok result
    //    tryDbFun g


    ///// TODO kk:20200523 - So far this looks extremely far beyond ugly.
    ///// Find the proper way and don't go beyond this one table until that proper way is found.
    /////
    ///// Here are some references:
    /////     https://devonburriss.me/how-to-fsharp-pt-9/
    /////     https://isthisit.nz/posts/2019/sqlite-database-with-dapper-and-fsharp/
    /////     http://zetcode.com/csharp/sqlite/
    //let saveMessageSqlite (SqliteConnectionString connectionString) (m : Message) =
    //    let g() =
    //        use connectionString = new SQLiteConnection(connectionString)

    //        let sql = @"
    //            insert into Message
    //                (messageId
    //                ,senderId
    //                ,recipientId
    //                ,dataVersion
    //                ,deliveryTypeId
    //                ,messageData
    //                ,createdOn)
    //            values
    //                (@messageId
    //                ,@senderId
    //                ,@recipientId
    //                ,@dataVersion
    //                ,@deliveryTypeId
    //                ,@messageData
    //                ,@createdOn)"

    //        let data =
    //            [
    //                ("@messageId", m.messageDataInfo.messageId.value.ToSqliteString() |> box)
    //                ("@senderId", m.messageDataInfo.sender.value.ToSqliteString() |> box)
    //                ("@recipientId", m.messageDataInfo.recipientInfo.recipient.value.ToSqliteString() |> box)
    //                ("@dataVersion", m.messageDataInfo.dataVersion.value |> box)
    //                ("@deliveryTypeId", m.messageDataInfo.recipientInfo.deliveryType.value |> box)
    //                ("@messageData", m.messageData |> (serialize serializationFormat) |> box)
    //                ("@createdOn", m.messageDataInfo.createdOn |> box)
    //            ]
    //            |> dict
    //            |> fun d -> DynamicParameters(d)

    //        let _ = executeSqlite connectionString sql data
    //        Ok()

    //    tryDbFun g


    //let deleteMessageSqlite connectionString (messageId : MessageId) =
    //    let toError e = e |> SendMessageErr |> MessagingClientErr |> Error

    //    let g() =
    //        use conn = getOpenSqliteConn connectionString
    //        use cmd = new SQLiteCommand("delete from Message where messageId = @messageId", conn)
    //        cmd.Parameters.Add(SQLiteParameter("@messageId", messageId.value.ToSqliteString())) |> ignore

    //        match cmd.ExecuteNonQuery() with
    //        | 0 | 1 -> Ok()
    //        | _ -> messageId |> SendMessageError.CannotDeleteMessageErr |> toError

    //    tryDbFun g


    //let deleteExpiredMessagesSqlite connectionString (expirationTime : TimeSpan) =
    //    let g() =
    //        use conn = getOpenSqliteConn connectionString
    //        use cmd = new SQLiteCommand(@"
    //            delete from Message
    //            where
    //                deliveryTypeId = 1
    //                and dataVersion = @dataVersion
    //                and createdOn < @createdOn", conn)

    //        cmd.Parameters.Add(SQLiteParameter("@dataVersion", messagingDataVersion.value)) |> ignore
    //        cmd.Parameters.Add(SQLiteParameter("@createdOn", DateTime.Now - expirationTime)) |> ignore

    //        let _ = cmd.ExecuteNonQuery()
    //        Ok()

    //    tryDbFun g


    //type SQLiteDataReader
    //    with
    //    member rdr.GetGuid(columnName : string) = rdr.GetString(rdr.GetOrdinal(columnName)) |> Guid.Parse
    //    member rdr.GetInt16(columnName : string) = rdr.GetInt16(rdr.GetOrdinal(columnName))
    //    member rdr.GetInt32(columnName : string) = rdr.GetInt32(rdr.GetOrdinal(columnName))
    //    member rdr.GetInt64(columnName : string) = rdr.GetInt64(rdr.GetOrdinal(columnName))
    //    member rdr.GetDateTime(columnName : string) = rdr.GetDateTime(rdr.GetOrdinal(columnName))
    //    member rdr.GetBoolean(columnName : string) = rdr.GetBoolean(rdr.GetOrdinal(columnName))

    //    member rdr.GetBlob(columnName : string) =
    //        let len = rdr.GetBytes(rdr.GetOrdinal(columnName), 0L, null, 0, Int32.MaxValue) |> int
    //        let bytes : byte array = Array.zeroCreate len
    //        rdr.GetBytes(rdr.GetOrdinal(columnName), 0L, bytes, 0, bytes.Length) |> ignore
    //        bytes


    //let toMessage (rdr : SQLiteDataReader) =
    //    {
    //        messageDataInfo =
    //            {
    //                messageId = rdr.GetGuid("messageId") |> MessageId
    //                dataVersion = rdr.GetInt32("dataVersion") |> MessagingDataVersion
    //                sender = rdr.GetGuid("senderId") |> MessagingClientId
    //                recipientInfo =
    //                    {
    //                        recipient = rdr.GetGuid("recipientId") |> MessagingClientId
    //                        deliveryType = rdr.GetInt32("deliveryTypeId")
    //                                       |> MessageDeliveryType.tryCreate
    //                                       |> Option.defaultValue GuaranteedDelivery
    //                    }

    //                createdOn = rdr.GetDateTime("createdOn")
    //            }

    //        messageData = rdr.GetBlob("messageData") |> (deserialize serializationFormat)
    //    }


    //let tryPickIncomingMessageSqlite connectionString (MessagingClientId i) =
    //    let g () =
    //        use conn = getOpenSqliteConn connectionString
    //        use cmd = new SQLiteCommand(@"
    //            select *
    //            from Message
    //            where recipientId = @recipientId and dataVersion = @dataVersion
    //            order by messageOrder
    //            limit 1", conn)

    //        cmd.Parameters.Add(SQLiteParameter("@recipientId", i.ToSqliteString())) |> ignore
    //        cmd.Parameters.Add(SQLiteParameter("@dataVersion", messagingDataVersion.value)) |> ignore
    //        use rdr = cmd.ExecuteReader()

    //        match rdr.Read() with
    //        | true -> toMessage rdr |> Some
    //        | false -> None
    //        |> Ok

    //    tryDbFun g


    //let tryPickOutgoingMessageSqlite connectionString (MessagingClientId i) =
    //    let g () =
    //        use conn = getOpenSqliteConn connectionString
    //        use cmd = new SQLiteCommand(@"
    //            select *
    //            from Message
    //            where senderId = @senderId and dataVersion = @dataVersion
    //            order by messageOrder
    //            limit 1", conn)

    //        cmd.Parameters.Add(SQLiteParameter("@senderId", i.ToSqliteString())) |> ignore
    //        cmd.Parameters.Add(SQLiteParameter("@dataVersion", messagingDataVersion.value)) |> ignore
    //        use rdr = cmd.ExecuteReader()

    //        match rdr.Read() with
    //        | true -> toMessage rdr |> Some
    //        | false -> None
    //        |> Ok

    //    tryDbFun g
