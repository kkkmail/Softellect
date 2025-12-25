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
open Softellect.Vpn.Core.UdpProtocol

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

        member private r.tryGetClientConfig (clientId : VpnClientId) =
            let keyId = KeyId clientId.value
            let keyFileName = FileName $"{clientId.value}.pkx"
            let clientKeysPath = data.serverAccessInfo.clientKeysPath
            let keyFilePath = keyFileName.combine clientKeysPath

            match tryImportPublicKey keyFilePath (Some keyId) with
            | Ok (_, publicKey) ->
                match tryLoadVpnClientConfig clientId with
                | Ok config ->
                    Logger.logInfo $"Loaded client: '{config.clientName.value}' : '{clientId.value}' -> '{config.assignedIp.value}', useEncryption: {config.useEncryption}, encryptionType: {config.encryptionType}."
                    Ok (config, publicKey)
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

        /// Create a push session for a client.
        member r.createPushSession(clientId: VpnClientId) : Result<PushClientSession, VpnError> =
            match r.tryGetClientConfig(clientId) with
            | Ok (config, publicKey) ->
                match tryAllocateSessionId clientId with
                | Ok sessionId ->
                    let sessionAesKey = generateSessionAesKey()

                    let session =
                        {
                            clientId = clientId
                            sessionId = sessionId
                            sessionAesKey = sessionAesKey
                            clientName = config.clientName
                            assignedIp = config.assignedIp
                            publicKey = publicKey
                            useEncryption = config.useEncryption
                            encryptionType = config.encryptionType
                            lastSeen = DateTime.UtcNow
                            currentEndpoint = None
                            pendingPackets = BoundedPacketQueue(PushQueueMaxBytes, PushQueueMaxPackets)
                            sendSeq = 0u
                            lastActivity = DateTime.UtcNow
                        }

                    sessionsBySessionId[sessionId] <- session
                    sessionIdByClientId[clientId] <- sessionId
                    Logger.logInfo $"Created push session for client: '{clientId.value}' with sessionId: {sessionId.value}, useEncryption: {config.useEncryption}."
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

        // /// Check if a push session's endpoint is fresh (within freshness timeout).
        // member _.isPushEndpointFresh(clientId: VpnClientId) : bool =
        //     match pushSessions.TryGetValue(clientId) with
        //     | true, session ->
        //         match session.currentEndpoint with
        //         | Some _ ->
        //             let age = DateTime.UtcNow - session.lastSeen
        //             age.TotalSeconds < float PushSessionFreshnessSeconds
        //         | None -> false
        //     | false, _ -> false

        // /// Enqueue a packet for a push client. Returns true if enqueued, false if no session or queue rejected.
        // member _.enqueuePushPacket(clientId: VpnClientId, packet: byte[]) : bool =
        //     match pushSessions.TryGetValue(clientId) with
        //     | true, session ->
        //         session.pendingPackets.enqueue(packet)
        //     | false, _ -> false

        // /// Get the next send sequence number for a push client.
        // member _.getNextPushSeq(clientId: VpnClientId) : uint32 =
        //     match pushSessions.TryGetValue(clientId) with
        //     | true, session ->
        //         let seq = session.sendSeq
        //         session.sendSeq <- session.sendSeq + 1u
        //         seq
        //     | false, _ -> 0u

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
