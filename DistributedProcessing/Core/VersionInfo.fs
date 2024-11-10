namespace Softellect.DistributedProcessing

open Softellect.Messaging.Primitives

module VersionInfo =

    /// The version of the messaging data is fixed because we are sending the data as byte array.
    let messagingDataVersion = MessagingDataVersion 1
