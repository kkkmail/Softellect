namespace Softellect.DistributedProcessing

open Softellect.Messaging.Primitives

module VersionInfo =

    /// !!! Update all non empty appsettings.json files to match this value !!!
    /// This is used in database and folder names.
    [<Literal>]
    let VersionNumberNumericalValue = "803_01"


    /// The version of the messaging data is fixed because we are sending the data as byte array.
    let messagingDataVersion = MessagingDataVersion 1
