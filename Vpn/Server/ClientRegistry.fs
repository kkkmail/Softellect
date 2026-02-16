namespace Softellect.Vpn.Server

open System
open System.Collections.Concurrent
open System.Net
open System.Security.Cryptography
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.Logging
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Transport.UdpProtocol
open Softellect.Sys.Core
open System.IO

module ClientRegistry =

    /// AES session key size in bytes (256-bit key).
    [<Literal>]
    let SessionAesKeySize = 32


    /// Client session for the push-based dataplane (spec 041).
    type PushClientSession =
        {
            clientId : VpnClientId
            sessionId : VpnSessionId
            sessionAesKey : byte[]
            clientName : VpnClientName
            assignedIp : VpnIpAddress
            publicKey : PublicKey
            useEncryption : bool
            hash : VpnClientHash option
            encryptionType : EncryptionType
            mutable lastSeen : DateTime
            mutable currentEndpoint : IPEndPoint option
            pendingPackets : BoundedPacketQueue
            mutable sendSeq : uint32
            mutable lastActivity : DateTime
        }


    type ClientRegistryData =
        {
            serverAccessInfo : VpnServerAccessInfo
            serverPrivateKey : PrivateKey
            serverPublicKey : PublicKey
        }


    type ClientRegistry(data: ClientRegistryData) =
        let sessionsBySessionId = ConcurrentDictionary<VpnSessionId, PushClientSession>()

        /// A revers lookup by clientId
        let sessionIdByClientId = ConcurrentDictionary<VpnClientId, VpnSessionId>()

        // Track kicked sessions to avoid repeated logging (spec 041: log error once)
        let kickedSessionsBySessionId = ConcurrentDictionary<VpnSessionId, DateTime>()

        let mutable nextSessionId = VpnSessionId 1uy
        let sessionIdLock = obj()

        let removeSession clientId =
            match sessionIdByClientId.TryGetValue clientId with
            | true, s ->
                match sessionsBySessionId.TryRemove s with
                | true, _ ->
                    Logger.logInfo $"Removed session for client: '{clientId.value}', session: {s.value}."
                    ()
                | false, _ -> ()
                match sessionIdByClientId.TryRemove clientId with
                | true, _ ->
                    Logger.logInfo $"Removed reverse session lookup for client: '{clientId.value}', session: {s.value}."
                    ()
                | false, _ -> ()
            | false, _ -> ()

        let removeSessionBySessionId sessionId =
            match sessionsBySessionId.TryGetValue sessionId with
            | true, session ->
                match sessionIdByClientId.TryRemove session.clientId with
                | true, _ ->
                    Logger.logInfo $"Removed session for client: '{session.clientId.value}', session: {sessionId.value}."
                    ()
                | false, _ -> ()
                match sessionsBySessionId.TryRemove sessionId with
                | true, _ ->
                    Logger.logInfo $"Removed reverse session lookup for client: '{session.clientId.value}', session: {sessionId.value}."
                    ()
                | false, _ -> ()
            | false, _ -> ()

        /// Allocate the next available session ID (1-255).
        let tryAllocateSessionId (clientId : VpnClientId) =
            removeSession clientId

            lock sessionIdLock (fun () ->
                let startId = nextSessionId
                let mutable found = false
                let mutable err = false
                let mutable result = VpnSessionId 0uy

                while not found && not err do
                    if not (sessionsBySessionId.ContainsKey(nextSessionId)) then
                        result <- nextSessionId
                        found <- true

                    nextSessionId <- if nextSessionId.value = 255uy then VpnSessionId 1uy else VpnSessionId (nextSessionId.value + 1uy)

                    if nextSessionId = startId && not found then
                        err <- true

                if err then NoAvailableSessionsErr |> VpnAuthErr |> VpnConnectionErr |> Error
                else Ok result
            )

        /// Generate a cryptographically random AES session key.
        let generateSessionAesKey () =
            let key = Array.zeroCreate SessionAesKeySize
            use rng = RandomNumberGenerator.Create()
            rng.GetBytes(key)
            key

        let getClientHashFilePath (clientId : VpnClientId) =
            let hashFileName = FileName $"{clientId.value}.hash"
            let hashFilePath = hashFileName.combine data.serverAccessInfo.clientKeysPath
            hashFilePath

        let tryGetClientHash (clientId : VpnClientId) =
            let hashFile = getClientHashFilePath clientId
            try
                match hashFile.tryGetFullFileName() with
                | Ok fn ->
                    match File.Exists fn.value with
                    | true ->
                        let hash = File.ReadAllText fn.value |> fun s -> s.Trim() |> VpnClientHash
                        Ok (Some hash)
                    | false -> Ok None
                | Error e ->
                    let err = $"Failed to load hash for client '{clientId.value}', error: '%A{e}'."
                    Logger.logWarn err
                    err |> HashErr |> Error
            with
            | e ->
                let err = $"Failed to load hash for client: '{clientId.value}', exception: '%A{e}'."
                Logger.logWarn err
                err |> HashErr |> Error

        /// Spec 056: Store client hash to file. Only creates if file doesn't exist.
        /// Returns Ok true if created, Ok false if already exists, Error on failure.
        let tryStoreClientHash (clientId : VpnClientId) (hash : VpnClientHash) =
            let hashFile = getClientHashFilePath clientId
            try
                match hashFile.tryGetFullFileName() with
                | Ok fn ->
                    if File.Exists fn.value then
                        // File already exists - do not overwrite
                        Ok false
                    else
                        // Create new hash file (ASCII, no BOM)
                        File.WriteAllText(fn.value, hash.value, System.Text.Encoding.ASCII)
                        Logger.logInfo $"Stored hash for client '{clientId.value}'."
                        Ok true
                | Error e ->
                    let err = $"Failed to store hash for client '{clientId.value}', error: '%A{e}'."
                    Logger.logWarn err
                    err |> HashErr |> Error
            with
            | e ->
                let err = $"Failed to store hash for client: '{clientId.value}', exception: '%A{e}'."
                Logger.logWarn err
                err |> HashErr |> Error


        member private r.tryGetClientConfigData (clientId : VpnClientId) =
            let keyId = KeyId clientId.value
            let keyFileName = FileName $"{clientId.value}.pkx"
            let clientKeysPath = data.serverAccessInfo.clientKeysPath
            let keyFilePath = keyFileName.combine clientKeysPath

            match tryImportPublicKey keyFilePath (Some keyId) with
            | Ok (_, publicKey) ->
                match tryLoadVpnClientConfig clientId with
                | Ok config ->
                    match tryGetClientHash clientId with
                    | Ok hash ->
                        Logger.logInfo $"Loaded client: '{config.clientName.value}' : '{clientId.value}' -> '{config.assignedIp.value}', useEncryption: {config.useEncryption}, encryptionType: {config.encryptionType}."
                        {
                            clientConfig = config
                            clientPublicKey = publicKey
                            clientHash = hash
                        }
                        |> Ok
                    | Error e -> Error e
                | Error e -> Error e
            | Error e ->
                Logger.logWarn $"Failed to load public key for client {clientId.value}: '%A{e}'."
                $"Failed to load public key for client {clientId.value}: '%A{e}'" |> ConfigErr |> Error

        member r.updateActivity(sessionId : VpnSessionId) =
            match r.tryGetPushSession sessionId with
            | Some session -> session.lastActivity <- DateTime.UtcNow
            | None -> ()

        member r.enqueuePacketForClient(sessionId : VpnSessionId, packet: byte[]) =
            match r.tryGetPushSession sessionId with
            | Some session -> session.pendingPackets.enqueue(packet)
            | None -> false

        member _.serverPrivateKey = data.serverPrivateKey
        member _.serverPublicKey = data.serverPublicKey

        /// Spec 056: Verify client hash binding.
        /// - If no stored hash exists, store the provided hash (first-use binding).
        /// - If stored hash exists, verify it matches the provided hash.
        /// Returns Ok () if verification passes (or binding succeeds), Error if mismatch.
        member _.verifyAndBindClientHash(clientId: VpnClientId, requestHash: VpnClientHash) : Result<unit, VpnError> =
            match tryGetClientHash clientId with
            | Ok (Some storedHash) ->
                // Hash exists - verify it matches
                if storedHash.value = requestHash.value then
                    Logger.logTrace (fun () -> $"Hash verification passed for client '{clientId.value}'.")
                    Ok ()
                else
                    let err = $"Hash mismatch for client '{clientId.value}': stored hash differs from request hash."
                    Logger.logWarn err
                    err |> HashMismatchErr |> VpnAuthErr |> VpnConnectionErr |> Error
            | Ok None ->
                // No stored hash - first-use binding
                match tryStoreClientHash clientId requestHash with
                | Ok _ ->
                    Logger.logInfo $"First-use hash binding for client '{clientId.value}'."
                    Ok ()
                | Error e -> Error e
            | Error e -> Error e

        /// Create a push session for a client.
        member r.createPushSession(clientId: VpnClientId) : Result<PushClientSession, VpnError> =
            match r.tryGetClientConfigData(clientId) with
            | Ok config ->
                match tryAllocateSessionId clientId with
                | Ok sessionId ->
                    let sessionAesKey = generateSessionAesKey()

                    let session =
                        {
                            clientId = clientId
                            sessionId = sessionId
                            sessionAesKey = sessionAesKey
                            clientName = config.clientConfig.clientName
                            assignedIp = config.clientConfig.assignedIp
                            publicKey = config.clientPublicKey
                            useEncryption = config.clientConfig.useEncryption
                            encryptionType = config.clientConfig.encryptionType
                            hash = config.clientHash
                            lastSeen = DateTime.UtcNow
                            currentEndpoint = None
                            pendingPackets = BoundedPacketQueue(PushQueueMaxBytes, PushQueueMaxPackets)
                            sendSeq = 0u
                            lastActivity = DateTime.UtcNow
                        }

                    sessionsBySessionId[sessionId] <- session
                    sessionIdByClientId[clientId] <- sessionId
                    Logger.logInfo $"Created push session for client: '{clientId.value}' with sessionId: {sessionId.value}, useEncryption: {config.clientConfig.useEncryption}."
                    Ok session
                | Error e -> Error e

            | Error e ->
                Logger.logWarn $"Push client not found: '{clientId.value}', error: '%A{e}'."
                clientId |> ClientNotFoundErr |> VpnAuthErr |> VpnConnectionErr |> Error

        /// Try to get an existing push session by sessionId.
        member _.tryGetPushSession(sessionId : VpnSessionId) : PushClientSession option =
            match sessionsBySessionId.TryGetValue sessionId with
            | true, session -> Some session
            | false, _ -> None

        /// Update push session endpoint and lastSeen.
        member r.updatePushEndpoint(sessionId : VpnSessionId, endpoint: IPEndPoint) =
            match r.tryGetPushSession sessionId with
            | Some session ->
                session.lastSeen <- DateTime.UtcNow
                session.currentEndpoint <- Some endpoint
            | None -> ()

        /// Expose kick sessions.
        member _.kickedSessions = kickedSessionsBySessionId

        /// Get all push sessions with pending packets and fresh endpoints.
        member _.getPushSessionsWithPendingPackets() : PushClientSession list =
            sessionsBySessionId.Values
            |> Seq.filter (fun s ->
                s.pendingPackets.count > 0 &&
                s.currentEndpoint.IsSome &&
                (DateTime.UtcNow - s.lastSeen).TotalSeconds < float PushSessionFreshnessSeconds)
            |> Seq.toList

        /// Remove a push session.
        member _.removePushSession(sessionId : VpnSessionId) =
            removeSessionBySessionId sessionId

        /// Get all push sessions.
        member _.getAllPushSessions() =
            sessionsBySessionId.Values |> Seq.toList
