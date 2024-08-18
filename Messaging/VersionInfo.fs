namespace Softellect.Messaging

open Softellect.Sys.Primitives
open Softellect.Messaging.Primitives

module VersionInfo =

    /// !!! Do not forget to update versionNumber in VersionInfo.ps1 when this parameter is updated !!!
    ///
    /// This is an overall system version.
    [<Literal>]
    let VersionNumberValue = "8.0.3.01"


    /// !!! Update all non empty appsettings.json files to match this value !!!
    /// The same as above but without the dots in order to use in database and folder names.
    [<Literal>]
    let private VersionNumberNumericalValue = "803_01"


    /// Messaging service name.
    [<Literal>]
    let MsgSvcBaseName = "msg" + VersionNumberNumericalValue


    /// Default port on which messaging communication is performed.
    let getMsgDefaultServicePort (MessagingDataVersion v) = 5000 + v |> ServicePort


    let versionNumberValue = VersionNumber VersionNumberValue

    let getDefaultMessagingNetTcpServicePort v = 40000 + (getMsgDefaultServicePort v).value |> ServicePort
    let getDefaultMessagingHttpServicePort v = (getDefaultMessagingNetTcpServicePort v).value + 1 |> ServicePort
    let defaultMessagingServiceAddress = localHost |> ServiceAddress
