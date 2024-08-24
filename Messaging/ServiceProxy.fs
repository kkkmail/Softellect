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
            messagingDataVersion : MessagingDataVersion
            storageType : MessagingClientStorageType
        }


    let createMessagingClientProxy<'D> getMessageSize (i : MessagingClientProxyInfo) (c : MessagingClientId) =
        let getMessageSize (e : MessageData<'D>) =
            match e with
            | SystemMsg _ -> SmallSize
            | UserMsg m -> getMessageSize m

        match i.storageType with
        | MsSqlDatabase g ->
            let v = i.messagingDataVersion

            {
                tryPickIncomingMessage = fun () -> tryPickIncomingMessage g v c
                tryPickOutgoingMessage = fun () -> tryPickOutgoingMessage g v c
                saveMessage = saveMessage g v
                tryDeleteMessage = deleteMessage g
                deleteExpiredMessages = deleteExpiredMessages g v
                getMessageSize = getMessageSize
                logger = Logger.defaultValue
            }
        //| SqliteDatabase connectionString ->
        //    {
        //        tryPickIncomingMessage = fun () -> tryPickIncomingMessageSqlite connectionString c
        //        tryPickOutgoingMessage = fun () -> tryPickOutgoingMessageSqlite connectionString c
        //        saveMessage = saveMessageSqlite connectionString
        //        tryDeleteMessage = deleteMessageSqlite connectionString
        //        deleteExpiredMessages = deleteExpiredMessagesSqlite connectionString
        //        getMessageSize = getMessageSize
        //        logger = Logger.defaultValue
        //    }


    let createMessagingServiceProxy (g : unit -> ConnectionString) (v : MessagingDataVersion) =
        {
            tryPickMessage = tryPickIncomingMessage g v
            saveMessage = saveMessage g v
            deleteMessage = deleteMessage g
            deleteExpiredMessages = deleteExpiredMessages g v
            logger = Logger.defaultValue
        }
