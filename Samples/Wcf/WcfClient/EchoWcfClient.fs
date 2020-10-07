namespace Softellect.Samples.Wcf.Client

open Softellect.Sys.Errors
open Softellect.Wcf.Common
open Softellect.Wcf.Client

open Softellect.Samples.Wcf.ServiceInfo.EchoWcfServiceInfo

module EchoWcfClient =

    type EchoWcfResponseHandler (i: ServiceAccessInfo) =
        //let tryGetWcfService() = tryGetWcfService<IEchoWcfService> i.netTcpUrl
        let tryGetWcfService() = tryGetWcfService<IEchoWcfService> i.httpUrl

        let echoWcfErr e = WcfErr e
        let echoImpl m = tryCommunicate tryGetWcfService (fun service -> service.echo) echoWcfErr m

        let complexEchoWcfErr e = WcfErr e
        let complexEchoImpl m = tryCommunicate tryGetWcfService (fun service -> service.complexEcho) complexEchoWcfErr m

        interface IEchoService with
            member _.echo m = echoImpl m
            member _.complexEcho m = complexEchoImpl m
        