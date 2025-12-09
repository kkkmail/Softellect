namespace Softellect.Vpn.Server

open System
open System.Collections.Concurrent
open System.IO
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Sys.Logging
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
        }


    type ClientRegistryData =
        {
            serverAccessInfo : VpnServerAccessInfo
            serverPrivateKey : PrivateKey
            serverPublicKey : PublicKey
        }


    type ClientRegistry(data: ClientRegistryData) =
        let sessions = ConcurrentDictionary<VpnClientId, ClientSession>()
        let clientConfigs = ConcurrentDictionary<VpnClientId, VpnClientConfig * PublicKey>()

        let loadClientKeys () =
            let clientKeysPath = data.serverAccessInfo.clientKeysPath.value

            if Directory.Exists clientKeysPath then
                let files = Directory.GetFiles(clientKeysPath, "*.pkx")

                for file in files do
                    try
                        match tryImportPublicKey (FileName file) None with
                        | Ok (keyId, publicKey) ->
                            let clientId = VpnClientId keyId.value
                            Logger.logInfo $"Loaded client key for {clientId.value}"
                            // Client config would be loaded from separate config
                            ()
                        | Error e ->
                            Logger.logWarn $"Failed to load client key from {file}: %A{e}"
                    with
                    | ex -> Logger.logWarn $"Exception loading client key from {file}: {ex.Message}"
            else
                Logger.logWarn $"Client keys path does not exist: {clientKeysPath}"

        do loadClientKeys()

        member _.RegisterClient(clientId: VpnClientId, config: VpnClientConfig, publicKey: PublicKey) =
            clientConfigs.[clientId] <- (config, publicKey)
            Logger.logInfo $"Registered client: {clientId.value} with IP {config.assignedIp.value}"

        member _.TryGetClientConfig(clientId: VpnClientId) =
            match clientConfigs.TryGetValue(clientId) with
            | true, (config, key) -> Some (config, key)
            | false, _ -> None

        member _.CreateSession(clientId: VpnClientId) : Result<ClientSession, VpnError> =
            match clientConfigs.TryGetValue(clientId) with
            | true, (config, publicKey) ->
                let session =
                    {
                        clientId = clientId
                        clientName = config.clientName
                        assignedIp = config.assignedIp
                        publicKey = publicKey
                        lastActivity = DateTime.UtcNow
                        pendingPackets = ConcurrentQueue<byte[]>()
                    }

                sessions.[clientId] <- session
                Logger.logInfo $"Created session for client {clientId.value}"
                Ok session
            | false, _ ->
                Logger.logWarn $"Client not found: {clientId.value}"
                clientId |> ClientNotFoundErr |> AuthFailedErr |> ConnectionErr |> Error

        member _.TryGetSession(clientId: VpnClientId) =
            match sessions.TryGetValue(clientId) with
            | true, session -> Some session
            | false, _ -> None

        member _.UpdateActivity(clientId: VpnClientId) =
            match sessions.TryGetValue(clientId) with
            | true, session ->
                sessions.[clientId] <- { session with lastActivity = DateTime.UtcNow }
            | false, _ -> ()

        member _.RemoveSession(clientId: VpnClientId) =
            sessions.TryRemove(clientId) |> ignore
            Logger.logInfo $"Removed session for client {clientId.value}"

        member _.EnqueuePacketForClient(clientId: VpnClientId, packet: byte[]) =
            match sessions.TryGetValue(clientId) with
            | true, session ->
                session.pendingPackets.Enqueue(packet)
                true
            | false, _ -> false

        member _.DequeuePacketsForClient(clientId: VpnClientId, maxPackets: int) =
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

        member _.GetAllSessions() =
            sessions.Values |> Seq.toList

        member _.ServerPrivateKey = data.serverPrivateKey
        member _.ServerPublicKey = data.serverPublicKey
