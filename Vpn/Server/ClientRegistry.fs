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

    /// Client session for the old polling-based dataplane.
    type ClientSession =
        {
            clientId : VpnClientId
            clientName : VpnClientName
            assignedIp : VpnIpAddress
            publicKey : PublicKey
            lastActivity : DateTime
            pendingPackets : ConcurrentQueue<byte[]>
            packetsAvailable : System.Threading.SemaphoreSlim
        }


    /// Client session for the push-based dataplane (spec 037).
    type PushClientSession =
        {
            clientId : VpnClientId
            clientName : VpnClientName
            assignedIp : VpnIpAddress
            publicKey : PublicKey
            mutable lastSeen : DateTime
            mutable currentEndpoint : IPEndPoint option
            pendingPackets : BoundedPacketQueue
            mutable sendSeq : uint32
        }


    type ClientRegistryData =
        {
            serverAccessInfo : VpnServerAccessInfo
            serverPrivateKey : PrivateKey
            serverPublicKey : PublicKey
        }


    type ClientRegistry(data: ClientRegistryData) =
        let sessions = ConcurrentDictionary<VpnClientId, ClientSession>()

        /// Push sessions dictionary
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

        member r.createSession(clientId: VpnClientId) : Result<ClientSession, VpnError> =
            match r.tryGetClientConfig(clientId) with
            | Ok (config, publicKey) ->
                let session =
                    {
                        clientId = clientId
                        clientName = config.clientName
                        assignedIp = config.assignedIp
                        publicKey = publicKey
                        lastActivity = DateTime.UtcNow
                        pendingPackets = ConcurrentQueue<byte[]>()
                        packetsAvailable = new System.Threading.SemaphoreSlim(0, Int32.MaxValue)
                    }

                sessions[clientId] <- session
                Logger.logInfo $"Created session for client {clientId.value}"
                Ok session
            | Error e ->
                Logger.logWarn $"Client not found: '{clientId.value}', error: '%A{e}'."
                clientId |> ClientNotFoundErr |> AuthFailedErr |> ConnectionErr |> Error

        member _.tryGetSession(clientId: VpnClientId) =
            match sessions.TryGetValue(clientId) with
            | true, session -> Some session
            | false, _ -> None

        member _.updateActivity(clientId: VpnClientId) =
            match sessions.TryGetValue(clientId) with
            | true, session ->
                sessions[clientId] <- { session with lastActivity = DateTime.UtcNow }
            | false, _ -> ()

        member _.removeSession(clientId: VpnClientId) =
            sessions.TryRemove(clientId) |> ignore
            Logger.logInfo $"Removed session for client {clientId.value}"

        member _.enqueuePacketForClient(clientId: VpnClientId, packet: byte[]) =
            // Try push session first (preferred for push dataplane).
            match pushSessions.TryGetValue(clientId) with
            | true, pushSession ->
                pushSession.pendingPackets.Enqueue(packet)
            | false, _ ->
                // Fall back to legacy session.
                match sessions.TryGetValue(clientId) with
                | true, session ->
                    session.pendingPackets.Enqueue(packet)
                    session.packetsAvailable.Release() |> ignore
                    true
                | false, _ ->
                    false

        member _.dequeuePacketsForClient(clientId: VpnClientId, maxPackets: int) =
            match sessions.TryGetValue(clientId) with
            | true, session ->
                let packets = ResizeArray<byte[]>()
                let mutable count = 0

                while count < maxPackets do
                    match session.pendingPackets.TryDequeue() with
                    | true, packet ->
                        packets.Add(packet)
                        count <- count + 1
                    | false, _ ->
                        count <- maxPackets // Exit loop

                packets.ToArray()
            | false, _ -> [||]

        member _.getAllSessions() =
            sessions.Values |> Seq.toList

        member _.serverPrivateKey = data.serverPrivateKey
        member _.serverPublicKey = data.serverPublicKey


        // ==================================================================
        // PUSH DATAPLANE METHODS (spec 037)
        // ==================================================================

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
                        lastSeen = DateTime.UtcNow
                        currentEndpoint = None
                        pendingPackets = BoundedPacketQueue(PushQueueMaxBytes, PushQueueMaxPackets)
                        sendSeq = 0u
                    }

                pushSessions[clientId] <- session
                Logger.logInfo $"Created push session for client {clientId.value}"
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
                session.pendingPackets.Enqueue(packet)
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
                s.pendingPackets.Count > 0 &&
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
