namespace Softellect.Vnc.Adm

open System
open System.IO
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.CryptoHelpers

module Program =

    let private generateServerKeys (outputFolder: string) =
        let folder = FolderName outputFolder
        if not (Directory.Exists outputFolder) then
            Directory.CreateDirectory(outputFolder) |> ignore

        let keyId = KeyId (Guid.NewGuid())
        let (publicKey, privateKey) = generateKey keyId

        match tryExportPrivateKey folder privateKey true with
        | Ok keyFile ->
            match tryExportPublicKey folder publicKey true with
            | Ok () ->
                printfn $"Server keys generated in: {outputFolder}"
                printfn $"  KeyId: {keyId.value}"
                printfn $"  Private key: {keyFile.value}"
                printfn $"  Public key: {keyId.value}.pkx"
                0
            | Error e ->
                printfn $"Error exporting public key: %A{e}"
                1
        | Error e ->
            printfn $"Error exporting private key: %A{e}"
            1


    let private generateViewerKeys (outputFolder: string) (serverPkxFolder: string) =
        let folder = FolderName outputFolder
        if not (Directory.Exists outputFolder) then
            Directory.CreateDirectory(outputFolder) |> ignore

        let viewerId = VncViewerId.create()
        let keyId = KeyId viewerId.value
        let (publicKey, privateKey) = generateKey keyId

        match tryExportPrivateKey folder privateKey true with
        | Ok keyFile ->
            match tryExportPublicKey folder publicKey true with
            | Ok () ->
                // Also export viewer's public key to the server's viewer keys folder
                if serverPkxFolder <> "" then
                    if not (Directory.Exists serverPkxFolder) then
                        Directory.CreateDirectory(serverPkxFolder) |> ignore
                    match tryExportPublicKey (FolderName serverPkxFolder) publicKey true with
                    | Ok () ->
                        printfn $"Viewer public key also copied to: {serverPkxFolder}"
                    | Error e ->
                        printfn $"Warning: could not copy viewer public key to server folder: %A{e}"

                printfn $"Viewer keys generated in: {outputFolder}"
                printfn $"  ViewerId: {viewerId.value}"
                printfn $"  Private key: {keyFile.value}"
                printfn $"  Public key: {viewerId.value}.pkx"
                0
            | Error e ->
                printfn $"Error exporting public key: %A{e}"
                1
        | Error e ->
            printfn $"Error exporting private key: %A{e}"
            1


    let private printUsage () =
        printfn "VNC Key Administration Tool"
        printfn ""
        printfn "Usage:"
        printfn "  VncAdm gen-server-keys [output-folder]"
        printfn "    Generate RSA key pair for the VNC service."
        printfn "    Default output: Keys/Server"
        printfn ""
        printfn "  VncAdm gen-viewer-keys [output-folder] [server-viewers-folder]"
        printfn "    Generate RSA key pair for a VNC viewer."
        printfn "    Default output: Keys/Viewer"
        printfn "    If server-viewers-folder is specified, the viewer's public key"
        printfn "    is also copied there for the server to authorize the viewer."
        printfn ""


    [<EntryPoint>]
    let main argv =
        if argv.Length = 0 then
            printUsage ()
            0
        else
            match argv.[0] with
            | "gen-server-keys" ->
                let folder = if argv.Length > 1 then argv.[1] else "Keys/Server"
                generateServerKeys folder
            | "gen-viewer-keys" ->
                let folder = if argv.Length > 1 then argv.[1] else "Keys/Viewer"
                let serverFolder = if argv.Length > 2 then argv.[2] else ""
                generateViewerKeys folder serverFolder
            | cmd ->
                printfn $"Unknown command: {cmd}"
                printUsage ()
                1
