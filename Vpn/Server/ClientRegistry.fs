namespace Softellect.Vpn.Server

open System
open System.Collections.Concurrent
open System.Net
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.Logging
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Vpn.Core.UdpProtocol

module ClientRegistry =

    /// Client session for the push-based dataplane (spec 037).
    type PushClientSession =
        {
            clientId : VpnClientId
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
        let pushSessions = ConcurrentDictionary<VpnClientId, PushClientSession>()

        member private _.tryGetClientConfig(clientId : VpnClientId) =
            let keyId = KeyId clientId.value
            let keyFileName = FileName $"{clientId.value}.pkx"
            let clientKeysPath = data.serverAccessInfo.clientKeysPath
            let keyFilePath = keyFileName.combine clientKeysPath

            match tryImportPublicKey keyFilePath (Some keyId) with
            | Ok (_, publicKey) ->
                match tryLoadVpnClientConfig clientId with
                | Ok config ->
                    Logger.logInfo $"Loaded client: '{config.clientName.value}' : '{clientId.value}' -> '{config.assignedIp.value}'."
                    Ok (config, publicKey)
                | Error e -> Error e
            | Error e ->
                Logger.logWarn $"Failed to load public key for client {clientId.value}: '%A{e}'."
                $"Failed to load public key for client {clientId.value}: '%A{e}'" |> ConfigErr |> Error

        member _.updateActivity(clientId: VpnClientId) =
            match pushSessions.TryGetValue(clientId) with
            | true, session -> session.lastActivity <- DateTime.UtcNow
            | false, _ -> ()

        member _.enqueuePacketForClient(clientId: VpnClientId, packet: byte[]) =
            match pushSessions.TryGetValue(clientId) with
            | true, pushSession -> pushSession.pendingPackets.enqueue(packet)
            | false, _ -> false

        member _.serverPrivateKey = data.serverPrivateKey
        member _.serverPublicKey = data.serverPublicKey

        /// Create a push session for a client.
        member r.createPushSession(clientId: VpnClientId) : Result<PushClientSession, VpnError> =
            match r.tryGetClientConfig(clientId) with
            | Ok (config, publicKey) ->
                let session =
                    {
                        clientId = clientId
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

                pushSessions[clientId] <- session
                Logger.logInfo $"Created push session for client {clientId.value} (useEncryption={config.useEncryption})"
                Ok session
            | Error e ->
                Logger.logWarn $"Push client not found: '{clientId.value}', error: '%A{e}'."
                clientId |> ClientNotFoundErr |> AuthFailedErr |> ConnectionErr |> Error

        /// Try to get an existing push session.
        member _.tryGetPushSession(clientId: VpnClientId) : PushClientSession option =
            match pushSessions.TryGetValue(clientId) with
            | true, session -> Some session
            | false, _ -> None

        /// Update push session endpoint and lastSeen.
        member _.updatePushEndpoint(clientId: VpnClientId, endpoint: IPEndPoint) =
            match pushSessions.TryGetValue(clientId) with
            | true, session ->
                session.lastSeen <- DateTime.UtcNow
                session.currentEndpoint <- Some endpoint
            | false, _ -> ()

        /// Check if a push session's endpoint is fresh (within freshness timeout).
        member _.isPushEndpointFresh(clientId: VpnClientId) : bool =
            match pushSessions.TryGetValue(clientId) with
            | true, session ->
                match session.currentEndpoint with
                | Some _ ->
                    let age = DateTime.UtcNow - session.lastSeen
                    age.TotalSeconds < float PushSessionFreshnessSeconds
                | None -> false
            | false, _ -> false

        /// Enqueue a packet for a push client. Returns true if enqueued, false if no session or queue rejected.
        member _.enqueuePushPacket(clientId: VpnClientId, packet: byte[]) : bool =
            match pushSessions.TryGetValue(clientId) with
            | true, session ->
                session.pendingPackets.enqueue(packet)
            | false, _ -> false

        /// Get the next send sequence number for a push client.
        member _.getNextPushSeq(clientId: VpnClientId) : uint32 =
            match pushSessions.TryGetValue(clientId) with
            | true, session ->
                let seq = session.sendSeq
                session.sendSeq <- session.sendSeq + 1u
                seq
            | false, _ -> 0u

        /// Get all push sessions with pending packets and fresh endpoints.
        member _.getPushSessionsWithPendingPackets() : PushClientSession list =
            pushSessions.Values
            |> Seq.filter (fun s ->
                s.pendingPackets.count > 0 &&
                s.currentEndpoint.IsSome &&
                (DateTime.UtcNow - s.lastSeen).TotalSeconds < float PushSessionFreshnessSeconds)
            |> Seq.toList

        /// Remove a push session.
        member _.removePushSession(clientId: VpnClientId) =
            pushSessions.TryRemove(clientId) |> ignore
            Logger.logInfo $"Removed push session for client {clientId.value}"

        /// Get all push sessions.
        member _.getAllPushSessions() =
            pushSessions.Values |> Seq.toList
