#nowarn "1104"

namespace Softellect.Messaging

open System
open FSharp.Data.Sql
open Softellect.Sys.AppSettings
open Softellect.Sys.Logging
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


    let private getConnectionStringImpl() = getConnectionString connectionStringKey ConnectionStringValue
    let private connectionString = Lazy<ConnectionString>(getConnectionStringImpl)
    let private getConnectionString() = connectionString.Value


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


    let saveMessage<'D> (v: MessagingDataVersion) (m: Message<'D>) =
        Logger.logTrace (fun () -> $"saveMessage: %A{m.messageDataInfo}")
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
                Logger.logError $"saveMessage: No row inserted - duplicate messageId %A{m.messageDataInfo.messageId}."
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
                    Logger.logTrace (fun () -> $"saveMessage: messageId %A{m.messageDataInfo.messageId} inserted.")
                    Ok()
                with
                | ex ->
                    Logger.logError $"saveMessage: Failed to insert messageId %A{m.messageDataInfo.messageId} with exception: %s{ex.Message}."
                    Error <| elevate (SaveMessageDbErr (DbExn ex))

        let result = tryDbFun fromDbError g
        Logger.logTrace (fun () -> $"saveMessage: %A{m.messageDataInfo}, result: %A{result}.")
        result


    let deleteMessage (messageId : MessageId) =
        Logger.logTrace (fun () -> $"deleteMessage: %A{messageId}")
        let elevate e = e |> DeleteMessageErr
        let toError e = e |> CannotDeleteMessageErr |> elevate
        let fromDbError e = e |> DeleteMessageDbErr |> elevate

        let g() =
            let ctx = getDbContext getConnectionString
            let r = ctx.Procedures.DeleteMessage.Invoke(``@messageId`` = messageId.value)
            r.ResultSet |> bindIntScalar toError messageId

        let result = tryDbFun fromDbError g
        Logger.logTrace (fun () -> $"deleteMessage: %A{messageId}, result: %A{result}.")
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
