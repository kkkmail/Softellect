namespace Softellect.Samples.Wcf.WcfClient

open Softellect.Sys.GeneralErrors
open Softellect.Wcf.Common
open Softellect.Wcf.Client
open Softellect.Samples.Wcf.WcfServiceInfo.EchoWcfServiceInfo

module EchoWcfClient =

    type EchoWcfResponseHandler (i: ServiceAccessInfo) =
        let tryGetWcfService() = tryGetWcfService<IEchoWcfService> i.netTcpUrl

        let echoWcfErr (e : WcfError) = e
        let echoImpl m = tryCommunicate tryGetWcfService (fun service -> service.echo) echoWcfErr m

        let complexEchoWcfErr (e : WcfError) = e
        let complexEchoImpl m = tryCommunicate tryGetWcfService (fun service -> service.complexEcho) complexEchoWcfErr m

        interface IEchoService with
            member _.echo m = echoImpl m
            member _.complexEcho m = complexEchoImpl m
        