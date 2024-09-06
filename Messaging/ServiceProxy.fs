namespace Softellect.Messaging

open Softellect.Messaging.Primitives
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Messaging.Proxy
open Softellect.Messaging.DataAccess

module ServiceProxy =

    type MessagingClientStorageType =
        //| MsSqlDatabase of (unit -> ConnectionString)
        //| SqliteDatabase of SqliteConnectionString
        | MsSqlDatabase


    type MessagingClientProxyInfo =
        {
            //messagingClientName : MessagingClientName
            messagingClientId : MessagingClientId
            messagingDataVersion : MessagingDataVersion
            storageType : MessagingClientStorageType
        }


    let createMessagingClientProxy<'D> getLogger getMessageSize (i : MessagingClientProxyInfo) =
        let getMessageSize (e : MessageData<'D>) =
            match e with
            | SystemMsg _ -> SmallSize
            | UserMsg m -> getMessageSize m

        match i.storageType with
        | MsSqlDatabase ->
            let v = i.messagingDataVersion

            {
                tryPickIncomingMessage = fun () -> tryPickIncomingMessage v i.messagingClientId
                tryPickOutgoingMessage = fun () -> tryPickOutgoingMessage v i.messagingClientId
                saveMessage = saveMessage v
                tryDeleteMessage = deleteMessage
                deleteExpiredMessages = deleteExpiredMessages v
                getMessageSize = getMessageSize
                getLogger = getLogger
            }
        //| SqliteDatabase connectionString ->
        //    {
        //        tryPickIncomingMessage = fun () -> tryPickIncomingMessageSqlite connectionString i.messagingClientId
        //        tryPickOutgoingMessage = fun () -> tryPickOutgoingMessageSqlite connectionString i.messagingClientId
        //        saveMessage = saveMessageSqlite connectionString
        //        tryDeleteMessage = deleteMessageSqlite connectionString
        //        deleteExpiredMessages = deleteExpiredMessagesSqlite connectionString
        //        getMessageSize = getMessageSize
        //        logger = Logger.defaultValue
        //    }


    let createMessagingServiceProxy getLogger (v : MessagingDataVersion) =
        {
            tryPickMessage = tryPickIncomingMessage v
            saveMessage = saveMessage v
            deleteMessage = deleteMessage
            deleteExpiredMessages = deleteExpiredMessages v
            getLogger = getLogger
        }
