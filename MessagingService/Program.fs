namespace Softellect.MessagingService

open Softellect.Messaging.Service
open Microsoft.FSharp.Core.Operators
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.AppSettings
open Softellect.Sys.Logging
open Softellect.Wcf.Program
open Softellect.Sys.AppSettings

module Program =

    let messagingMain<'D> messagingProgramName data argv =
        let postBuildHandler _ _ =
            Logger.logInfo $"main<{typeof<'D>.Name}> - data.messagingServiceAccessInfo = '{data.messagingServiceAccessInfo}'."

        let saveSettings() =
            let result = updateMessagingServiceAccessInfo data.messagingServiceAccessInfo
            Logger.logInfo $"saveSettings - result: '%A{result}'."

        let projectName = getProjectName() |> Some

        let programData =
            {
                serviceAccessInfo = data.messagingServiceAccessInfo.serviceAccessInfo
                getService = fun () -> new MessagingService<'D>(data) :> IMessagingService<'D>
                getWcfService = fun service -> new MessagingWcfService<'D>(service)
                saveSettings = saveSettings
                configureServices = None
                configureServiceLogging = configureServiceLogging projectName
                configureLogging = configureLogging projectName
                postBuildHandler = Some postBuildHandler
            }

        wcfMain<IMessagingService<'D>, IMessagingWcfService, MessagingWcfService<'D>> messagingProgramName programData argv
