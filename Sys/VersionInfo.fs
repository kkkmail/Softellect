namespace Softellect.Sys

open Softellect.Sys.Primitives
open Softellect.Sys.MessagingPrimitives

module VersionInfo =

    /// !!! Do not forget to update messagingDataVersion in VersionInfo.ps1 when this parameter is updated !!!
    ///
    /// Increment BY TWO when:
    ///     1. Internal messaging structures change and messages can no longer be successfully transferred among components.
    ///     2. Some other updates were performed and we need to inform worker nodes that they need to upgrade.
    ///     3. Version number (below) was increased.
    ///     4. Reset to 0 as needed.
    [<Literal>]
    let private MessagingDataVersionValue = 0


    let messagingDataVersion = MessagingDataVersion MessagingDataVersionValue


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
    [<Literal>]
    let MsgDefaultServicePort = 5000 + MessagingDataVersionValue


    [<Literal>]
    let CopyrightInfo = "MIT License - Copyright Konstantin K. Konstantinov and Alisa F. Konstantinova © 2015 - 2024."


    type VersionNumber =
        | VersionNumber of string

        member this.value = let (VersionNumber v) = this in v


    let versionNumberValue = VersionNumber VersionNumberValue

    let defaultMessagingNetTcpServicePort = 40000 + MsgDefaultServicePort
    let defaultMessagingHttpServicePort = defaultMessagingNetTcpServicePort + 1
    let defaultMessagingServiceAddress = LocalHost
