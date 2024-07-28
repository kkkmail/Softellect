namespace Softellect.WorkerNode

open System
open System.Threading

open Softellect.Messaging.Primitives
open Softellect.Messaging.Errors
open Softellect.Sys.Rop
open Softellect.Sys.TimerEvents

open Softellect.Wcf.Common
open Softellect.Wcf.Client

open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Proxy

module VersionInfo =

    /// !!! Update all non empty appsettings.json files to match this value !!!
    /// The same as above but without the dots in order to use in database and folder names.
    [<Literal>]
    let private VersionNumberNumericalValue = "803_01"


    [<Literal>]
    let WorkerNodeSvcBaseName = "wns" + VersionNumberNumericalValue
