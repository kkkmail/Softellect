namespace Softellect.Vpn.Server

open System
open System.Collections.Concurrent
open System.IO
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.Logging
open Softellect.Vpn.Core.AppSettings
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo

module ClientRegistry =

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


    type ClientRegistryData =
        {
            serverAccessInfo : VpnServerAccessInfo
            serverPrivateKey : PrivateKey
            serverPublicKey : PublicKey
        }


    type ClientRegistry(data: ClientRegistryData) =
        let sessions = ConcurrentDictionary<VpnClientId, ClientSession>()

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
                        packetsAvailable = new System.Threading.SemaphoreSlim(0, System.Int32.MaxValue)
                    }

                sessions.[clientId] <- session
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
                sessions.[clientId] <- { session with lastActivity = DateTime.UtcNow }
            | false, _ -> ()

        member _.removeSession(clientId: VpnClientId) =
            sessions.TryRemove(clientId) |> ignore
            Logger.logInfo $"Removed session for client {clientId.value}"

        member _.enqueuePacketForClient(clientId: VpnClientId, packet: byte[]) =
            match sessions.TryGetValue(clientId) with
            | true, session ->
                session.pendingPackets.Enqueue(packet)
                session.packetsAvailable.Release() |> ignore
                true
            | false, _ -> false

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
