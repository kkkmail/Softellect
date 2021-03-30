﻿namespace Softellect.Samples.Wcf.Service

open System

open Softellect.Wcf.Service

open Softellect.Samples.Wcf.ServiceInfo.EchoWcfErrors
open Softellect.Samples.Wcf.ServiceInfo.EchoWcfServiceInfo

module EchoWcfService =

    type EchoService (data : EchoServiceData) =
        let getReply m =
            {
                a = m.x + data.data
                b = [ DateTime.Now.Hour; DateTime.Now.Minute; DateTime.Now.Second ]
                echoType = A
            }

        let echoImpl (m : string) : UnitResult =
            printfn $"Simple message: %A{m}"
            Ok()

        let complexEchoImpl (m : EchoMessage) : EchoWcfResult<EchoReply> =
            printfn $"Complex message: %A{m}"
            m |> getReply |> Ok

        interface IEchoService with
            member _.echo m = echoImpl m
            member _.complexEcho m = complexEchoImpl m


    type EchoWcfService private (data : EchoServiceData) =
        static let getData() = EchoServiceData.create()
        let service = EchoService(data) :> IEchoService
        let toEchoError f = f |> EchoWcfErr
        let toComplexEchoError f = f |> EchoWcfErr

        new() = EchoWcfService(getData())

        interface IEchoWcfService with
            member _.echo m = tryReply service.echo toEchoError m
            member _.complexEcho m = tryReply service.complexEcho toComplexEchoError m


    type EchoWcfServiceImpl = WcfService<EchoWcfService, IEchoWcfService, EchoServiceData>
