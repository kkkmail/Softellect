namespace Softellect.Sys

open System
open System.Threading
open System.Configuration.Install
open System.ServiceProcess
open Argu

open Softellect.Sys.Logging
open Softellect.Sys.GeneralErrors
open Softellect.Sys.Errors
open Softellect.Sys.Primitives

module ServiceInstaller =

    [<Literal>]
    let ServiceTmeOut = 10_000.0


    type ServiceInstallerLogger = Logger<ServiceInstallerError>


    type ServiceInfo<'R, 'C> =
        {
            serviceName : ServiceName
            runService : ServiceInstallerLogger -> 'R -> 'C option
            cleanup : ServiceInstallerLogger -> 'C -> unit
            timeoutMilliseconds : int option
            logger : ServiceInstallerLogger
        }

        member this.timeout =
            match this.timeoutMilliseconds with
            | Some t -> float t
            | None -> ServiceTmeOut
            |> TimeSpan.FromMilliseconds


    /// kk:20190812 - As of this date Argu does not support generics.
    /// Which means that it is not possible to use this class directly and we need to remap the values.
    [<CliPrefix(CliPrefix.None)>]
    type SvcArguments<'A when 'A :> IArgParserTemplate> =
        | [<Unique>] [<First>] [<AltCommandLine("i")>] Install
        | [<Unique>] [<First>] [<AltCommandLine("u")>] Uninstall
        | [<Unique>] [<First>] Start
        | [<Unique>] [<First>] Stop
        | [<Unique>] [<First>] [<AltCommandLine("r")>] Run of ParseResults<'A>
        | [<Unique>] [<First>] [<AltCommandLine("s")>] Save of ParseResults<'A>

        static member fromArgu c a : list<SvcArguments<'A>> = a |> List.map (fun e -> c e)


    let private toError logError e =
        e |> SingleErr |> logError
        false


    /// https://stackoverflow.com/questions/31081879/writing-a-service-in-f
    let private getInstaller<'T> () =
        let installer = new AssemblyInstaller(typedefof<'T>.Assembly, null);
        installer.UseNewContext <- true
        installer


    let private installService<'T> (l : ServiceInstallerLogger) (ServiceName serviceName) =
        try
            l.logInfoString (sprintf "Attempting to install service %s ..." serviceName)
            let i = getInstaller<'T> ()
            let d = System.Collections.Hashtable()
            i.Install(d)
            i.Commit(d)
            l.logInfoString "... services installed successfully.\n"
            true
        with
        | e -> e |> InstallServiceErr |> toError l.logErrData


    let private uninstallService<'T> (l : ServiceInstallerLogger) (ServiceName serviceName) =
        try
            l.logInfoString (sprintf "Attempting to uninstall service %s ..." serviceName)
            let i = getInstaller<'T> ()
            let d = System.Collections.Hashtable()
            i.Uninstall(d)
            l.logInfoString "... services uninstalled successfully.\n"
            true
        with
        | e -> e |> UninstallServiceErr |> toError l.logErrData


    let private startService (i : ServiceInfo<'R, 'C>) =
        try
            i.logger.logInfoString (sprintf "Attempting to start service %s ..." i.serviceName.value)
            let service = new ServiceController(i.serviceName.value)
            service.Start ()
            service.WaitForStatus(ServiceControllerStatus.Running, i.timeout)
            i.logger.logInfoString (sprintf "... service %s started successfully.\n" i.serviceName.value)
            true
        with
        | e -> e |> StartServiceErr |> toError i.logger.logErrData


    let private stopService (i : ServiceInfo<'R, 'C>) =
        try
            i.logger.logInfoString (sprintf "Attempting to stop service %s ..." i.serviceName.value)
            let service = new ServiceController(i.serviceName.value)
            service.Stop ()
            service.WaitForStatus(ServiceControllerStatus.Stopped, i.timeout)
            i.logger.logInfoString (sprintf "... service %s stopped successfully.\n" i.serviceName.value)
            true
        with
        | e -> e |> StopServiceErr |> toError i.logger.logErrData


    let private runService (i : ServiceInfo<'R, 'C>) r =
        i.logger.logInfoString "Starting..."
        let waitHandle = new ManualResetEvent(false)
        let c = i.runService i.logger r

        let cancelHandler() =
            match c with
            | Some v ->
                i.logger.logInfoString (sprintf "Performing cleanup for %s ..." i.serviceName.value)
                i.cleanup i.logger v
            | None -> i.logger.logInfoString (sprintf "NOT performing cleanup for %s because the service was not created..." i.serviceName.value)

            waitHandle.Set() |> ignore

        Console.CancelKeyPress.Add (fun _ -> cancelHandler())
        waitHandle.WaitOne() |> ignore
        true


    type ServiceTask<'T, 'R, 'A when 'A :> IArgParserTemplate> =
        | InstallServiceTask
        | UninstallServiceTask
        | StartServiceTask
        | StopServiceTask
        | RunServiceTask of 'R
        | SaveSettingsTask of (unit -> unit)

        member task.run (i : ServiceInfo<'R, 'C>) =
            match task with
            | InstallServiceTask -> installService<'T> i.logger i.serviceName
            | UninstallServiceTask ->
                match stopService i with
                | true -> i.logger.logInfoString (sprintf "Successfully stopped service %s." i.serviceName.value)
                | false -> i.logger.logInfoString (sprintf "Failed to stop service %s! Proceeding with uninstall anyway." i.serviceName.value)

                uninstallService<'T> i.logger i.serviceName
            | StartServiceTask -> startService i
            | StopServiceTask -> stopService i
            | RunServiceTask r -> runService i r
            | SaveSettingsTask s ->
                s()
                true

        static member private tryCreateInstallServiceTask (p : list<SvcArguments<'A>>) : ServiceTask<'T, 'R, 'A> option =
            p |> List.tryPick (fun e -> match e with | Install -> InstallServiceTask |> Some | _ -> None)

        static member private tryCreateUninstallServiceTask (p : list<SvcArguments<'A>>) : ServiceTask<'T, 'R, 'A> option =
            p |> List.tryPick (fun e -> match e with | Uninstall -> UninstallServiceTask |> Some | _ -> None)

        static member private tryCreateStartServiceTask (p : list<SvcArguments<'A>>) :ServiceTask<'T, 'R, 'A> option =
            p |> List.tryPick (fun e -> match e with | Start -> StartServiceTask |> Some | _ -> None)

        static member private tryCreateStopServiceTask (p : list<SvcArguments<'A>>) : ServiceTask<'T, 'R, 'A> option =
            p |> List.tryPick (fun e -> match e with | Stop -> StopServiceTask |> Some | _ -> None)

        static member private tryCreateRunServiceTask r (p : list<SvcArguments<'A>>) : ServiceTask<'T, 'R, 'A> option =
            p |> List.tryPick (fun e -> match e with | Run p -> r p |> RunServiceTask |> Some | _ -> None)

        static member private tryCreateSaveSettingsTask s (p : list<SvcArguments<'A>>) : ServiceTask<'T, 'R, 'A> option =
            p |> List.tryPick (fun e -> match e with | Save p -> s p |> SaveSettingsTask |> Some | _ -> None)

        static member tryCreate r s p : ServiceTask<'T, 'R, 'A> option =
            [
                ServiceTask.tryCreateUninstallServiceTask
                ServiceTask.tryCreateInstallServiceTask
                ServiceTask.tryCreateStopServiceTask
                ServiceTask.tryCreateStartServiceTask
                ServiceTask.tryCreateRunServiceTask r
                ServiceTask.tryCreateSaveSettingsTask s
            ]
            |> List.tryPick (fun e -> e p)
