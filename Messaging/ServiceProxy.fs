namespace Softellect.Messaging

open Softellect.Sys.MessagingPrimitives
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.Messaging.Primitives
open Softellect.Messaging.Proxy
open Softellect.Messaging.DataAccess

module ServiceProxy =

    type MessagingClientStorageType =
        | MsSqlDatabase of (unit -> ConnectionString)
        //| SqliteDatabase of SqliteConnectionString


    type MessagingClientProxyInfo =
        {
            messagingClientName : MessagingClientName
            storageType : MessagingClientStorageType
        }


    let createMessagingClientProxy<'D> getMessageSize (i : MessagingClientProxyInfo) (c : MessagingClientId) =
        let getMessageSize (e : MessageData<'D>) =
            match e with
            | SystemMsg _ -> SmallSize
            | UserMsg m -> getMessageSize m

        match i.storageType with
        | MsSqlDatabase g ->
            {
                tryPickIncomingMessage = fun () -> tryPickIncomingMessage g c
                tryPickOutgoingMessage = fun () -> tryPickOutgoingMessage g c
                saveMessage = saveMessage g
                tryDeleteMessage = deleteMessage g
                deleteExpiredMessages = deleteExpiredMessages g
                getMessageSize = getMessageSize
                logger = Logger.defaultValue
                toErr = fun e -> e |> MessagingClientErr
                addError = fun a b -> a + b
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
        //        toErr = fun e -> e |> MessagingClientErr
        //        addError = fun a b -> a + b
        //    }


    let createMessagingServiceProxy (g : unit -> ConnectionString) =
        {
            tryPickMessage = tryPickIncomingMessage g
            saveMessage = saveMessage g
            deleteMessage = deleteMessage g
            deleteExpiredMessages = deleteExpiredMessages g
            logger = Logger.defaultValue
            toErr = fun e -> e |> MessagingServiceErr
        }
