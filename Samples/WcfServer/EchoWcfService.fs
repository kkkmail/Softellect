namespace Softellect.Communication.Samples

open System

open Softellect.Core.GeneralErrors
open Softellect.Communication.Wcf
open Softellect.Communication.WcfServer
open Softellect.Communication.Samples.EchoWcfServiceInfo

module EchoWcfService =

    type EchoService() =
        let getReply m =
            {
                a = m.x
                b = [ DateTime.Now.Hour; DateTime.Now.Minute; DateTime.Now.Second ]
                echoType = A
            }

        let echoImpl (m : string) : Result<unit, WcfError> =
            printfn "Simple message: %A" m
            Ok()

        let complexEchoImpl (m : EchoMessage) : Result<EchoReply, WcfError> =
            printfn "Complex message: %A" m
            m |> getReply |> Ok

        interface IEchoService with
            member _.echo m = echoImpl m
            member _.complexEcho m = complexEchoImpl m


    type EchoWcfService() =
        let service = EchoService() :> IEchoService
        let toEchoError f = f
        let toComplexEchoError f = f

        interface IEchoWcfService with
            member _.echo m = tryReply service.echo toEchoError m
            member _.complexEcho m = tryReply service.complexEcho toComplexEchoError m


    type EchoWcfServiceImpl = WcfService<EchoWcfService, IEchoWcfService>
