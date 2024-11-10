namespace Softellect.Messaging

open Softellect.Sys.Primitives
open Softellect.Messaging.Primitives
open Softellect.Sys.VersionInfo

module VersionInfo =

    /// Messaging service name.
    [<Literal>]
    let MsgSvcBaseName = "msg" + VersionNumberNumericalValue


    /// Default port on which messaging communication is performed.
    let getMsgDefaultServicePort (MessagingDataVersion v) = 5000 + v |> ServicePort


    let versionNumberValue = VersionNumber VersionNumberValue

    let getDefaultMessagingNetTcpServicePort v = 40000 + (getMsgDefaultServicePort v).value |> ServicePort
    let getDefaultMessagingHttpServicePort v = (getDefaultMessagingNetTcpServicePort v).value + 1 |> ServicePort
    let defaultMessagingServiceAddress = localHost |> ServiceAddress
