namespace Softellect.MessagingService

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open Softellect.Wcf.Common
open Softellect.Wcf.Service
open Softellect.MessagingService.SvcCommandLine
open Softellect.Sys.Logging
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.Service
open Softellect.Sys.Primitives
open Softellect.Messaging.VersionInfo


type MessagingWcfServiceImpl<'D> = WcfService<MessagingWcfService<'D>, IMessagingWcfService, MessagingServiceData<'D>>


//type MsgWorker<'D>(logger: ILogger<MsgWorker<'D>>) =
//    inherit BackgroundService()

//    static let messagingServiceData = getMessagingServiceData Logger.defaultValue

//    static let tyGetHost() =
//        match messagingServiceData.Value with
//        | Ok data ->
//            let service = MessagingWcfServiceImpl<'D>.tryGetService data
//            MessagingService<'D>.tryStart() |> ignore
//            service
//        | Error e -> Error e

//    static let hostRes = Lazy<WcfResult<WcfService>>(fun () -> tyGetHost())

//    override _.ExecuteAsync(_: CancellationToken) =
//        async {
//            logger.LogInformation("Executing...")

//            match hostRes.Value with
//            | Ok host -> do! host.runAsync()
//            | Error e -> logger.LogCritical$"Error: %A{e}"
//        }
//        |> Async.StartAsTask
//        :> Task

//    override _.StopAsync(_: CancellationToken) =
//        async {
//            logger.LogInformation("Stopping...")

//            match hostRes.Value with
//            | Ok host -> do! host.stopAsync()
//            | Error e -> logger.LogCritical$"Error: %A{e}"
//        }
//        |> Async.StartAsTask
//        :> Task


type MsgWorker<'D>(logger: ILogger<MsgWorker<'D>>) =
    inherit BackgroundService()

    static let tyGetHost() =
        printfn $"tyGetHost: Getting MessagingServiceData..."
        let messagingServiceData = getMessagingServiceData Logger.defaultValue

        match messagingServiceData with
        | Ok data ->
            printfn $"tyGetHost: Got MessagingServiceData: '{data}'."
            let service = MessagingWcfServiceImpl<'D>.tryGetService data
            MessagingService<'D>.tryStart() |> ignore
            service
        | Error e ->
            printfn $"tyGetHost: Error: %A{e}"
            Error e

    static let hostRes = Lazy<WcfResult<WcfService>>(fun () -> tyGetHost())

    override _.ExecuteAsync(_: CancellationToken) =
        async {
            logger.LogInformation("Executing...")

            match hostRes.Value with
            | Ok host -> do! host.runAsync()
            | Error e -> logger.LogCritical$"Error: %A{e}"
        }
        |> Async.StartAsTask
        :> Task

    override _.StopAsync(_: CancellationToken) =
        async {
            logger.LogInformation("Stopping...")

            match hostRes.Value with
            | Ok host -> do! host.stopAsync()
            | Error e -> logger.LogCritical$"Error: %A{e}"
        }
        |> Async.StartAsTask
        :> Task


//type MsgWorker<'D>(logger: ILogger<MsgWorker<'D>>) =
//    inherit BackgroundService()

//    static let hostRes =
//        let serviceAddress = ServiceAddress defaultMessagingServiceAddress
//        let httpServicePort = ServicePort defaultMessagingHttpServicePort
//        let httpServiceName = ServiceName "EchoMessagingHttpService"
//        let netTcpServicePort = ServicePort defaultMessagingNetTcpServicePort
//        let netTcpServiceName = ServiceName "EchoMessagingNetTcpService"

//        let httpServiceInfo = HttpServiceAccessInfo.create serviceAddress httpServicePort httpServiceName
//        let netTcpServiceInfo = NetTcpServiceAccessInfo.create serviceAddress netTcpServicePort netTcpServiceName WcfSecurityMode.defaultValue
//        let echoMsgServiceAccessInfo = ServiceAccessInfo.create httpServiceInfo netTcpServiceInfo

//        let serviceData =
//            {
//                messagingServiceInfo =
//                    {
//                        expirationTime = TimeSpan.FromSeconds 10.0
//                        messagingDataVersion = dataVersion
//                    }

//                communicationType = communicationType
//                messagingServiceProxy = serviceProxy
//            }

//        let echoMsgServiceDataRes = tryGetMsgServiceData echoMsgServiceAccessInfo Logger.defaultValue serviceData

//        match echoMsgServiceDataRes with
//        | Ok data ->
//            let service = MessagingWcfServiceImpl<'D>.tryGetService data

//            // Comment this line to make the service instantiated on first request.
//            MessagingService<'D>.tryStart() |> ignore
//            service
//        | Error e -> Error e

//    override _.ExecuteAsync(_: CancellationToken) =
//        async {
//            logger.LogInformation("Executing...")

//            match hostRes with
//            | Ok host -> do! host.runAsync()
//            | Error e -> logger.LogCritical$"Error: %A{e}"
//        }
//        |> Async.StartAsTask
//        :> Task

//    override _.StopAsync(_: CancellationToken) =
//        async {
//            logger.LogInformation("Stopping...")

//            match hostRes with
//            | Ok host -> do! host.stopAsync()
//            | Error e -> logger.LogCritical$"Error: %A{e}"
//        }
//        |> Async.StartAsTask
//        :> Task
