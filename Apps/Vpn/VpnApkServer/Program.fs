namespace Softellect.Vpn.VpnApkServer

open System
open System.IO
open System.Text
open System.Text.Encodings.Web
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
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

    type LargeFontDirectoryFormatter() =
        interface IDirectoryFormatter with
            member _.GenerateContentAsync(context: HttpContext, contents: seq<IFileInfo>) : Task =
                task {
                    let request = context.Request
                    let response = context.Response
                    response.ContentType <- "text/html; charset=utf-8"

                    let path = request.Path.Value
                    let sb = StringBuilder()

                    sb.AppendLine("<!DOCTYPE html>") |> ignore
                    sb.AppendLine("<html><head>") |> ignore
                    sb.AppendLine("<meta charset=\"utf-8\">") |> ignore
                    sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">") |> ignore
                    sb.AppendLine($"<title>{HtmlEncoder.Default.Encode(path)}</title>") |> ignore
                    sb.AppendLine("<style>") |> ignore
                    sb.AppendLine("body { font-family: sans-serif; font-size: 24px; padding: 20px; }") |> ignore
                    sb.AppendLine("a { display: block; padding: 15px 0; text-decoration: none; color: #0066cc; }") |> ignore
                    sb.AppendLine("a:hover { text-decoration: underline; }") |> ignore
                    sb.AppendLine("h1 { font-size: 28px; }") |> ignore
                    sb.AppendLine("</style>") |> ignore
                    sb.AppendLine("</head><body>") |> ignore
                    sb.AppendLine($"<h1>{HtmlEncoder.Default.Encode(path)}</h1>") |> ignore

                    if path <> "/" then
                        let segments = path.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                        let parent =
                            if segments.Length <= 1 then "/"
                            else "/" + String.Join("/", segments |> Array.take (segments.Length - 1)) + "/"
                        sb.AppendLine($"<a href=\"{parent}\">..</a>") |> ignore

                    for item in contents do
                        let name = item.Name
                        let href =
                            if item.IsDirectory then
                                path.TrimEnd('/') + "/" + name + "/"
                            else
                                path.TrimEnd('/') + "/" + name
                        sb.AppendLine($"<a href=\"{HtmlEncoder.Default.Encode(href)}\">{HtmlEncoder.Default.Encode(name)}</a>") |> ignore

                    sb.AppendLine("</body></html>") |> ignore

                    do! response.WriteAsync(sb.ToString())
                } :> Task

    let configureServices (services: IServiceCollection) =
        services.AddSingleton<IDirectoryFormatter, LargeFontDirectoryFormatter>() |> ignore
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
        directoryBrowserOptions.Formatter <- LargeFontDirectoryFormatter()

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
