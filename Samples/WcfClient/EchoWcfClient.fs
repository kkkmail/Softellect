namespace Softellect.Communication.Samples

open Softellect.Core.GeneralErrors
open Softellect.Communication.Wcf
open Softellect.Communication.Samples.EchoWcfServiceInfo

module EchoWcfClient =

    type EchoWcfResponseHandler (url) =
        let tryGetWcfService() = tryGetWcfService<IEchoWcfService> url

        let echoWcfErr (e : WcfError) = e
        let echoImpl m = tryCommunicate tryGetWcfService (fun service -> service.echo) echoWcfErr m

        let complexEchoWcfErr (e : WcfError) = e
        let complexEchoImpl m = tryCommunicate tryGetWcfService (fun service -> service.complexEcho) complexEchoWcfErr m

        interface IEchoService with
            member _.echo m = echoImpl m
            member _.complexEcho m = complexEchoImpl m
        