namespace Softellect.Sys

open Softellect.Sys.MessagingPrimitives

module DataAccessErrors =

    type DbError =
        | DbExn of exn
        | MessagingSvcSaveMessageErr of MessageId
        | MessagingSvcCannotDeleteMessageErr of MessageId
