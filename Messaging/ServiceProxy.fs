namespace Softellect.Messaging

open Softellect.Messaging.Primitives
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Messaging.Proxy
open Softellect.Messaging.DataAccess

module ServiceProxy =

    type MessagingClientStorageType =
        | MsSqlDatabase of (unit -> ConnectionString)
        //| SqliteDatabase of SqliteConnectionString


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
        | MsSqlDatabase g ->
            let v = i.messagingDataVersion

            {
                tryPickIncomingMessage = fun () -> tryPickIncomingMessage g v i.messagingClientId
                tryPickOutgoingMessage = fun () -> tryPickOutgoingMessage g v i.messagingClientId
                saveMessage = saveMessage g v
                tryDeleteMessage = deleteMessage g
                deleteExpiredMessages = deleteExpiredMessages g v
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


    let createMessagingServiceProxy getLogger (g : unit -> ConnectionString) (v : MessagingDataVersion) =
        {
            tryPickMessage = tryPickIncomingMessage g v
            saveMessage = saveMessage g v
            deleteMessage = deleteMessage g
            deleteExpiredMessages = deleteExpiredMessages g v
            getLogger = getLogger
        }
