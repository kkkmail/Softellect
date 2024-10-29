#nowarn "1104"

namespace Softellect.Messaging

open System
open FSharp.Data.Sql
open Softellect.Sys.AppSettings
open Softellect.Sys.Primitives
open Softellect.Messaging.VersionInfo
open Softellect.Sys.Core
open Softellect.Sys.DataAccess
open Softellect.Messaging.Errors
open Softellect.Messaging.Primitives
open Softellect.Sys.Errors

module DataAccess =

    let private serializationFormat = BinaryZippedFormat
    let private connectionStringKey = ConfigKey "MessagingService"


    [<Literal>]
    let private DbName = MsgSvcBaseName


    [<Literal>]
    let private ConnectionStringValue = "Server=localhost;Database=" + DbName + ";Integrated Security=SSPI;TrustServerCertificate=yes;"


    let private getConnectionStringImpl() =
        let c = getConnectionString appSettingsFile connectionStringKey ConnectionStringValue
        printfn $"getConnectionStringImpl: %A{c}."
        c

    let private connectionString = Lazy<ConnectionString>(getConnectionStringImpl)

    let private getConnectionString() =
        let c = connectionString.Value
        printfn $"getConnectionString: %A{c}."
        c


    //[<Literal>]
    //let private SqlProviderName : string = "name=MessagingService"


    //[<Literal>]
    //let private SqlProviderName = DefaultRootFolder


    //[<Literal>]
    //let SqliteConnStr = "Data Source=" + __SOURCE_DIRECTORY__ + @"\" + MsgDatabase + @";Version=3;foreign keys=true"


    //let private getSqlLiteConnStr msgDbLocation = @"Data Source=" + msgDbLocation + ";Version=3;foreign keys=true"
    ////let private msgSqliteConnStr = SqliteConnStr |> SqliteConnectionString


    //type Guid
    //    with
    //    member g.ToSqliteString() = g.ToString("N")


    //type sqLite = SqlDataProvider<
    //               Common.DatabaseProviderTypes.SQLITE,
    //               SQLiteLibrary = Common.SQLiteLibrary.SystemDataSQLite,
    //               ConnectionString = MsgSqliteConnStr,
    //               //ResolutionPath = resolutionPath,
    //               CaseSensitivityChange = Common.CaseSensitivityChange.ORIGINAL>


    type private Db = SqlDataProvider<
                    Common.DatabaseProviderTypes.MSSQLSERVER,
                    ConnectionString = ConnectionStringValue,
                    UseOptionTypes = Common.NullableColumnType.OPTION>


    type private DbContext = Db.dataContext
    type private MessageEntity = DbContext.``dbo.MessageEntity``


    let private getDbContext (c : unit -> ConnectionString) = c().value |> Db.GetDataContext


    let private tryCreateMessageImpl (v : MessagingDataVersion) (r : MessageEntity) =
        let elevate e = e |> TryCreateMessageErr
        let toError e = e |> elevate |> Error
        let fromDbError e = e |> TryCreateMessageDbErr |> elevate

        let g() =
            let messageId = r.MessageId |> MessageId

            match MessageDeliveryType.tryCreate r.DeliveryTypeId, v.value = r.DataVersion with
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
            | Some _, false -> InvalidDataVersionErr (messageId, { localVersion = v; remoteVersion = MessagingDataVersion r.DataVersion }) |> toError
            | None, true -> InvalidDeliveryTypeErr (messageId, r.DeliveryTypeId) |> toError
            | None, false -> InvalidDeliveryTypeAndDataVersionErr (messageId, r.DeliveryTypeId, { localVersion = v; remoteVersion = MessagingDataVersion r.DataVersion }) |> toError

        tryDbFun fromDbError g


    let tryCreateMessage (v : MessagingDataVersion) (t : MessageEntity option) =
        match t with
        | Some e -> e |> tryCreateMessageImpl v
        | None -> Ok None


    let tryPickIncomingMessage (v : MessagingDataVersion) (MessagingClientId i) =
        let fromDbError e = e |> TryPickIncomingMessageDbErr |> TryPickMessageErr

        let g () =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for m in ctx.Dbo.Message do
                    where (m.RecipientId = i && m.DataVersion = v.value)
                    sortBy m.MessageOrder
                    select (Some m)
                    headOrDefault
                }

            tryCreateMessage v x

        tryDbFun fromDbError g


    let tryPickOutgoingMessage (v : MessagingDataVersion) (MessagingClientId i) =
        let fromDbError e = e |> TryPickOutgoingMessageDbErr |> TryPickMessageErr

        let g () =
            let ctx = getDbContext getConnectionString

            let x =
                query {
                    for m in ctx.Dbo.Message do
                    where (m.SenderId = i && m.DataVersion = v.value)
                    sortBy m.MessageOrder
                    select (Some m)
                    headOrDefault
                }

            tryCreateMessage v x

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
    // let saveMessage<'D> (v : MessagingDataVersion) (m : Message<'D>) =
    //     printfn $"saveMessage: %A{m.messageDataInfo}"
    //     let elevate e = e |> SaveMessageErr
    //     let toError e = e |> CannotSaveMessageErr |> elevate
    //     let fromDbError e = e |> SaveMessageDbErr |> elevate
    //
    //     let g() =
    //         let ctx = getDbContext getConnectionString
    //
    //         let r = ctx.Procedures.SaveMessage.Invoke(
    //                         ``@messageId`` = m.messageDataInfo.messageId.value,
    //                         ``@senderId`` = m.messageDataInfo.sender.value,
    //                         ``@recipientId`` = m.messageDataInfo.recipientInfo.recipient.value,
    //                         ``@dataVersion`` = v.value,
    //                         ``@deliveryTypeId`` = m.messageDataInfo.recipientInfo.deliveryType.value,
    //                         ``@messageData`` = (m.messageData |> serialize serializationFormat))
    //
    //         r.ResultSet |> bindIntScalar toError m.messageDataInfo.messageId
    //
    //     let result = tryDbFun fromDbError g
    //     printfn $"saveMessage: %A{m.messageDataInfo}, result: %A{result}."
    //     result
    // let saveMessage<'D> (v : MessagingDataVersion) (m : Message<'D>) =
    //     printfn $"saveMessage: %A{m.messageDataInfo}"
    //     let elevate e = e |> SaveMessageErr
    //     let toError e = e |> CannotSaveMessageErr |> elevate
    //     let fromDbError e = e |> SaveMessageDbErr |> elevate
    //
    //     let g() =
    //         let ctx = getDbContext getConnectionString
    //
    //         let r = ctx.Procedures.SaveMessage.Invoke(
    //                         ``@messageId`` = m.messageDataInfo.messageId.value,
    //                         ``@senderId`` = m.messageDataInfo.sender.value,
    //                         ``@recipientId`` = m.messageDataInfo.recipientInfo.recipient.value,
    //                         ``@dataVersion`` = v.value,
    //                         ``@deliveryTypeId`` = m.messageDataInfo.recipientInfo.deliveryType.value,
    //                         ``@messageData`` = (m.messageData |> serialize serializationFormat))
    //
    //         let x =
    //             match r.ResultSet |> mapIntScalar with
    //             | Some 1 ->
    //                 printfn $"saveMessage: messageId with %A{m.messageDataInfo.messageId} inserted."
    //                 Ok()
    //             | Some v ->
    //                 printfn $"saveMessage: No row inserted - possible duplicate messageId or constraint issue, %A{m.messageDataInfo.messageId}, v = {v}."
    //                 Error <| elevate (CannotSaveMessageErr m.messageDataInfo.messageId)
    //             | None ->
    //                 printfn $"saveMessage: No row inserted - %A{m.messageDataInfo.messageId}, unknown error."
    //                 Error <| elevate (CannotSaveMessageErr m.messageDataInfo.messageId)
    //
    //         x
    //
    //     let result = tryDbFun fromDbError g
    //     printfn $"saveMessage: %A{m.messageDataInfo}, result: %A{result}."
    //     result
    let saveMessage<'D> (v: MessagingDataVersion) (m: Message<'D>) =
        printfn $"saveMessage: %A{m.messageDataInfo}"
        let elevate e = e |> SaveMessageErr
        let toError e = e |> CannotSaveMessageErr |> elevate
        let fromDbError e = e |> SaveMessageDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString

            // Check if the message with this ID already exists
            let existingMessage =
                query {
                    for msg in ctx.Dbo.Message do
                    where (msg.MessageId = m.messageDataInfo.messageId.value)
                    select (Some msg.MessageId)
                    exactlyOneOrDefault
                }

            match existingMessage with
            | Some _ ->
                printfn $"saveMessage: No row inserted - duplicate messageId %A{m.messageDataInfo.messageId}."
                Error <| elevate (CannotSaveMessageErr m.messageDataInfo.messageId)
            | None ->
                // Create a new Message and insert it
                let newMessage = ctx.Dbo.Message.Create()
                newMessage.MessageId <- m.messageDataInfo.messageId.value
                newMessage.SenderId <- m.messageDataInfo.sender.value
                newMessage.RecipientId <- m.messageDataInfo.recipientInfo.recipient.value
                newMessage.DataVersion <- v.value
                newMessage.DeliveryTypeId <- m.messageDataInfo.recipientInfo.deliveryType.value
                newMessage.MessageData <- m.messageData |> serialize serializationFormat
                newMessage.CreatedOn <- DateTime.UtcNow

                try
                    ctx.SubmitUpdates()
                    printfn $"saveMessage: messageId %A{m.messageDataInfo.messageId} inserted."
                    Ok()
                with
                | ex ->
                    printfn $"saveMessage: Failed to insert messageId %A{m.messageDataInfo.messageId} with exception: %s{ex.Message}."
                    Error <| elevate (SaveMessageDbErr (DbExn ex))

        let result = tryDbFun fromDbError g
        printfn $"saveMessage: %A{m.messageDataInfo}, result: %A{result}."
        result


    let deleteMessage (messageId : MessageId) =
        printfn $"deleteMessage: %A{messageId}"
        let elevate e = e |> DeleteMessageErr
        let toError e = e |> CannotDeleteMessageErr |> elevate
        let fromDbError e = e |> DeleteMessageDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.DeleteMessage .Invoke(``@messageId`` = messageId.value)
            r.ResultSet |> bindIntScalar toError messageId

        let result = tryDbFun fromDbError g
        printfn $"deleteMessage: %A{messageId}, result: %A{result}."
        result


    let deleteExpiredMessages (v : MessagingDataVersion) (expirationTime : TimeSpan) =
        let elevate e = e |> DeleteExpiredMessagesErr
        let fromDbError e = e |> DeleteExpiredMessagesDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.DeleteExpiredMessages.Invoke(``@dataVersion`` = v.value, ``@createdOn`` = DateTime.Now - expirationTime)
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
