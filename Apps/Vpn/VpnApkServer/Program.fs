namespace Softellect.Vpn.VpnApkServer

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.StaticFiles
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.AppSettings

module Program =

    let baseUrlKey = ConfigKey "BaseUrl"
    let defaultBaseUrl = "http://192.168.1.123:8088"

    let loadBaseUrl () =
        match AppSettingsProvider.tryCreate() with
        | Ok provider ->
            let baseUrl = provider.getStringOrDefault baseUrlKey defaultBaseUrl
            Logger.logInfo $"loadBaseUrl - BaseUrl: '{baseUrl}'."
            baseUrl
        | Error e ->
            Logger.logCrit $"loadBaseUrl - Cannot load settings. Error: '%A{e}'."
            failwith $"loadBaseUrl - Cannot load settings. Error: '%A{e}'."

    let getWebRootPath () =
        let exeDir = AppContext.BaseDirectory
        let wwwroot = Path.Combine(exeDir, "wwwroot")
        Logger.logInfo $"getWebRootPath - wwwroot absolute path: '{wwwroot}'."
        wwwroot

    let configureServices (services: IServiceCollection) =
        services.AddDirectoryBrowser() |> ignore

    let configureApp (webRootPath: string) (app: IApplicationBuilder) =
        let fileProvider = new PhysicalFileProvider(webRootPath)

        let staticFileOptions = StaticFileOptions()
        staticFileOptions.FileProvider <- fileProvider
        staticFileOptions.ServeUnknownFileTypes <- true

        let contentTypeProvider = FileExtensionContentTypeProvider()
        contentTypeProvider.Mappings.[".apk"] <- "application/vnd.android.package-archive"
        staticFileOptions.ContentTypeProvider <- contentTypeProvider

        let directoryBrowserOptions = DirectoryBrowserOptions()
        directoryBrowserOptions.FileProvider <- fileProvider

        app.UseStaticFiles(staticFileOptions) |> ignore
        app.UseDirectoryBrowser(directoryBrowserOptions) |> ignore

    [<EntryPoint>]
    let main args =
        setLogLevel()
        let projectName = getProjectName()
        Logger.logInfo $"VpnApkServer starting - ProjectName: '{projectName.value}'."

        let baseUrl = loadBaseUrl()
        let webRootPath = getWebRootPath()

        if not (Directory.Exists webRootPath) then
            Logger.logWarn $"wwwroot folder does not exist, creating: '{webRootPath}'."
            Directory.CreateDirectory(webRootPath) |> ignore

        Logger.logInfo $"VpnApkServer binding to: '{baseUrl}'."
        Logger.logInfo $"VpnApkServer serving files from: '{webRootPath}'."

        try
            let host =
                Host.CreateDefaultBuilder(args)
                    .ConfigureLogging(fun logging ->
                        configureLogging (Some projectName) logging)
                    .ConfigureWebHostDefaults(fun webBuilder ->
                        webBuilder
                            .UseUrls(baseUrl)
                            .ConfigureServices(configureServices)
                            .Configure(configureApp webRootPath)
                        |> ignore)
                    .Build()

            host.Run()
            Softellect.Sys.ExitErrorCodes.CompletedSuccessfully
        with
        | ex ->
            Logger.logCrit $"VpnApkServer failed to start: '{ex.Message}'."
            Softellect.Sys.ExitErrorCodes.CriticalError
