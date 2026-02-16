namespace Softellect.Vnc.Viewer

open System
open System.Windows.Forms
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.AppSettings
open Softellect.Wcf.Common
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.CryptoHelpers
open Softellect.Vnc.Core.ServiceInfo
open Softellect.Vnc.Viewer.ViewerForm

module Program =

    let private parseArgs (argv: string[]) =
        let mutable host = "127.0.0.1"
        let mutable port = DefaultVncWcfPort
        let mutable udpPort = DefaultVncUdpPort + 1000
        let mutable viewerKeyPath = "Keys/Viewer"
        let mutable serverPkxPath = "Keys/server.pkx"

        let mutable i = 0
        while i < argv.Length do
            match argv.[i] with
            | "--host" | "-h" when i + 1 < argv.Length ->
                host <- argv.[i + 1]
                i <- i + 2
            | "--port" | "-p" when i + 1 < argv.Length ->
                port <- int argv.[i + 1]
                i <- i + 2
            | "--udp-port" when i + 1 < argv.Length ->
                udpPort <- int argv.[i + 1]
                i <- i + 2
            | "--viewer-keys" when i + 1 < argv.Length ->
                viewerKeyPath <- argv.[i + 1]
                i <- i + 2
            | "--server-key" when i + 1 < argv.Length ->
                serverPkxPath <- argv.[i + 1]
                i <- i + 2
            | arg ->
                if arg.Contains(":") then
                    let parts = arg.Split(':')
                    host <- parts.[0]
                    port <- int parts.[1]
                elif not (arg.StartsWith("-")) then
                    host <- arg
                i <- i + 1

        (host, port, udpPort, viewerKeyPath, serverPkxPath)

    [<EntryPoint; STAThread>]
    let main argv =
        setLogLevel()
        let host, port, udpPort, viewerKeyPath, serverPkxPath = parseArgs argv

        Logger.logInfo $"VNC Viewer starting, connecting to {host}:{port}, local UDP port {udpPort}"

        match loadViewerKeys (FolderName viewerKeyPath) (FileName serverPkxPath) with
        | Ok (viewerId, viewerPrivateKey, viewerPublicKey, serverPublicKey) ->
            let viewerData : VncViewerData =
                {
                    viewerId = viewerId
                    viewerPrivateKey = viewerPrivateKey
                    viewerPublicKey = viewerPublicKey
                    serverPublicKey = serverPublicKey
                    encryptionType = EncryptionType.defaultValue
                }

            let serviceAccessInfo : ServiceAccessInfo =
                {
                    netTcpServiceAddress = ServiceAddress (Ip4 host)
                    netTcpServicePort = ServicePort port
                    netTcpServiceName = ServiceName VncServiceName
                    netTcpSecurityMode = NoSecurity
                }
                |> NetTcpServiceInfo

            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(false)

            use form = new VncViewerForm(viewerData, serviceAccessInfo, udpPort)
            Application.Run(form)
            0
        | Error msg ->
            Logger.logCrit $"Failed to load viewer keys: {msg}"
            MessageBox.Show($"Failed to load keys: {msg}", "Key Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore
            1
