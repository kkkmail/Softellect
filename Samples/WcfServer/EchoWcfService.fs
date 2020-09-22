namespace Softellect.Communication.Samples

open CoreWCF
open CoreWCF.Configuration
open System.Runtime.Serialization
open System.ServiceProcess
open System.ServiceModel

open Softellect.Core.GeneralErrors
open Softellect.Communication.Wcf
open Softellect.Communication.Samples.EchoWcfServiceInfo


module EchoWcfService =


    type EchoWcfService() =
        let toEchoError f = f
        let toComplexEchoError f = f

        let getReply m =
            {
                a = m.x
                b = [1; 2; 3]
                echoType = A
            }

        let echo (m : string) : Result<unit, WcfError> =
            printfn "Simple message: %A" m
            Ok()

        let complexEcho (m : EchoMessage) : Result<EchoReply, WcfError> =
            printfn "Complex message: %A" m
            m |> getReply |> Ok


        interface IEchoWcfService
            with
            member _.echo m = tryReply echo toEchoError m
            member _.complexEcho m = tryReply complexEcho toComplexEchoError m
