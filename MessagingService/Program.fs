namespace Softellect.MessagingService

open Softellect.Messaging.Service
open Microsoft.FSharp.Core.Operators
open Softellect.Messaging.ServiceInfo
open Softellect.Messaging.AppSettings
open Softellect.Wcf.Program

module Program =

    let messagingMain<'D> messagingProgramName data argv =
        printfn $"main<{typeof<'D>.Name}> - data.messagingServiceAccessInfo = '{data.messagingServiceAccessInfo}'."

        let saveSettings() =
            let result = updateMessagingServiceAccessInfo data.messagingServiceAccessInfo
            printfn $"saveSettings - result: '%A{result}'."

        let programData =
            {
                serviceAccessInfo = data.messagingServiceAccessInfo.serviceAccessInfo
                getService = fun () -> new MessagingService<'D>(data) :> IMessagingService<'D>
                getWcfService = fun service -> new MessagingWcfService<'D>(service)
                saveSettings = saveSettings
                configureServices = None
            }

        wcfMain<IMessagingService<'D>, IMessagingWcfService, MessagingWcfService<'D>> messagingProgramName programData argv
