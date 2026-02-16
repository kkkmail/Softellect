namespace Softellect.Vnc.Service

open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Sys.ExitErrorCodes
open Softellect.Sys.AppSettings
open Softellect.Vnc.Core.AppSettings
open Softellect.Vnc.Core.CryptoHelpers
open Softellect.Vnc.Core.ServiceInfo
open Softellect.Vnc.Service.VncServiceImpl
open Softellect.Vnc.Service.WcfServer

module Program =

    let vncServiceMain argv =
        setLogLevel()
        let accessInfo = loadVncServiceAccessInfo()
        let serverKeyPath = loadVncServerKeyPath()
        let viewerKeysPath = loadVncViewerKeysPath()

        Logger.logInfo $"vncServiceMain - serviceUrl='{accessInfo.serviceAccessInfo.getUrl()}', udpPort={accessInfo.udpPort}"

        match loadServerKeys serverKeyPath with
        | Ok (privateKey, publicKey) ->
            let data : VncServerData =
                {
                    vncServiceAccessInfo = accessInfo
                    serverPrivateKey = privateKey
                    serverPublicKey = publicKey
                    viewerKeysPath = viewerKeysPath
                    encryptionType = EncryptionType.defaultValue
                }

            let getService () = VncService(data)
            let program = getVncWcfProgram data getService argv
            program()
        | Error msg ->
            Logger.logCrit msg
            CriticalError


    [<EntryPoint>]
    let main argv =
        try
            vncServiceMain argv
        with
        | ex ->
            Logger.logCrit $"VNC Service fatal error: {ex.Message}"
            CriticalError
