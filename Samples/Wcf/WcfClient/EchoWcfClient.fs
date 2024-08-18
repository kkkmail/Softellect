namespace Softellect.Samples.Wcf.Client

open Softellect.Wcf.Common
open Softellect.Wcf.Client

open Softellect.Samples.Wcf.ServiceInfo.EchoWcfErrors
open Softellect.Samples.Wcf.ServiceInfo.EchoWcfServiceInfo

module EchoWcfClient =

    type EchoWcfResponseHandler (i: ServiceAccessInfo) =
        let url = i.getUrl()
        let tryGetWcfService() =  tryGetWcfService<IEchoWcfService> i.communicationType url
        let echoWcfErr e = e |> EchoWcfErr
        let echoImpl m = tryCommunicate tryGetWcfService (fun service -> service.echo) echoWcfErr m

        let complexEchoWcfErr e = e |> EchoWcfErr
        let complexEchoImpl m = tryCommunicate tryGetWcfService (fun service -> service.complexEcho) complexEchoWcfErr m

        interface IEchoService with
            member _.echo m = echoImpl m
            member _.complexEcho m = complexEchoImpl m
