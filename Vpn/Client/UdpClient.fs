namespace Softellect.Vpn.Client

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Softellect.Sys.Logging
open Softellect.Sys.Crypto
open Softellect.Vpn.Core.PacketDebug
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.ServiceInfo
open Softellect.Transport.UdpProtocol

module UdpClient =

    [<Literal>]
    let FragmentsYieldEvery = 32

    /// Response data passed through TCS: (cmd, clientId, logicalPayload).
    type private ResponseData = byte * VpnClientId * byte[]

    /// Record for pending request tracking.
    type private PendingRequest =
        {
            createdAtTicks : int64
            expectedCmd : byte
            clientId : VpnClientId
            tcs : TaskCompletionSource<ResponseData>
        }


    /// Interface for injecting packets into the tunnel (decouples from Tunnel module).
    type IPacketInjector =
        abstract injectPacket: byte[] -> Result<unit, string>


    /// Backoff delay when auth is not available (ms).
    [<Literal>]
    let NoAuthBackoffMs = 100


    /// Push dataplane UDP client (spec 041/042).
    /// Uses per-packet AES encryption with session key and nonce-based key derivation.
    /// Wire format: [sessionId: 1 byte][nonce: 16 bytes][payload]
    /// Per spec 042: Does not store session data. Uses getAuth() on each iteration.
    type VpnPushUdpClient(data: VpnClientServiceData, getAuth: unit -> VpnAuthResponse option) =
        let clientAccessInfo = data.clientAccessInfo.vpnConnectionInfo
        let serverIp = clientAccessInfo.serverAccessInfo.getIpAddress()
        let serverPort = clientAccessInfo.serverAccessInfo.getServicePort().value
        let serverEndpoint = IPEndPoint(serverIp.ipAddress, serverPort)
        let udpClient = new UdpClient()
        let clientCts = new CancellationTokenSource()
        let clientPushStats = SenderPushStats()

        // Encryption config
        let useEncryption = data.clientAccessInfo.useEncryption
        let vpnClientId = data.clientAccessInfo.vpnClientId

        // Bounded queue for outbound packets (from TUN to server).
        let outboundQueue = BoundedPacketQueue(PushQueueMaxBytes, PushQueueMaxPackets)

        // Bounded queue for inbound packets (from server to TUN).
        let inboundPacketQueue = BoundedPacketQueue(PushQueueMaxBytes, PushQueueMaxPackets)

        let mutable packetInjector : IPacketInjector option = None
        let mutable receiveTask : Task option = None
        let mutable sendTask : Task option = None
        let mutable keepaliveTask : Task option = None

        do
            // Bind to an ephemeral local port.
            udpClient.Client.Bind(IPEndPoint(IPAddress.Any, 0))

            // Connect to the server endpoint.
            udpClient.Connect(serverEndpoint)

            // Set receive timeout for periodic checks.
            udpClient.Client.ReceiveTimeout <- CleanupIntervalMs

            Logger.logInfo $"VpnPushUdpClient created - Server: {serverIp}:{serverPort}, ClientId: {vpnClientId.value}, UseEncryption: {useEncryption}, Local={udpClient.Client.LocalEndPoint}"

        /// Encrypt payload using per-packet AES key derivation.
        let encryptPayload (sessionAesKey: byte[]) (plaintextPayload: byte[]) (nonce: Guid) : Result<byte[], string> =
            if useEncryption then
                let aesKey = derivePacketAesKey sessionAesKey nonce
                match tryEncryptAesKey plaintextPayload aesKey with
                | Ok encrypted -> Ok encrypted
                | Error e -> Error $"AES encryption failed: %A{e}"
            else
                Ok plaintextPayload

        /// Decrypt payload using per-packet AES key derivation.
        let decryptPayload (sessionAesKey: byte[]) (encryptedPayload: byte[]) (nonce: Guid) : Result<byte[], string> =
            if useEncryption then
                let aesKey = derivePacketAesKey sessionAesKey nonce
                match tryDecryptAesKey encryptedPayload aesKey with
                | Ok decrypted -> Ok decrypted
                | Error e -> Error $"AES decryption failed: %A{e}"
            else
                Ok encryptedPayload

        /// UDP receive loop - receives pushed datagrams from the server.
        /// Per spec 042: calls getAuth() on each iteration; skips if None.
        let receiveLoop () =
            Logger.logInfo "Receive loop started."
            while not clientCts.Token.IsCancellationRequested do
                try
                    // Get the current auth snapshot
                    match getAuth() with
                    | None ->
                        // No auth available - backoff and retry
                        Thread.Sleep(NoAuthBackoffMs)
                    | Some auth ->
                        let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
                        let rawData = udpClient.Receive(&remoteEp)

                        clientPushStats.udpRxDatagrams.increment()
                        clientPushStats.udpRxBytes.addInt(rawData.Length)

                        match tryParsePushDatagram rawData with
                        | Ok (receivedPushSessionId, nonce, payloadBytes) ->
                            let receivedSessionId = VpnSessionId.fromPushSessionId receivedPushSessionId
                            // Verify sessionId matches current auth
                            if receivedSessionId <> auth.sessionId then
                                // Session mismatch - log and ignore (may be stale packet from old session)
                                Logger.logWarn $"Push client: SessionId mismatch: expected {auth.sessionId.value}, got {receivedSessionId.value} - ignoring packet"
                            else
                                // Decrypt if needed using the current auth's session key
                                match decryptPayload auth.sessionAesKey payloadBytes nonce with
                                | Ok plaintextPayload ->
                                    match tryParsePayload plaintextPayload with
                                    | Ok (cmd, cmdData) ->
                                        if cmd = PushCmdData && cmdData.Length > 0 then
                                            Logger.logTrace (fun () -> $"HEAVY LOG - Received: {cmdData.Length} bytes, cmdData: {(summarizePacket cmdData)}.")

                                            // Inject directly if injector is available, otherwise queue.
                                            match packetInjector with
                                            | Some injector ->
                                                match injector.injectPacket(cmdData) with
                                                | Ok () -> ()
                                                | Error msg ->
                                                    clientPushStats.droppedQueueFullInject.increment()
                                                    Logger.logWarn $"Push client: Failed to inject packet: {msg}"
                                            | None ->
                                                // Queue for later injection.
                                                if not (inboundPacketQueue.enqueue(cmdData)) then
                                                    clientPushStats.droppedQueueFullInject.increment()
                                        elif cmd = PushCmdKeepalive then
                                            Logger.logTrace (fun () -> "Push client: Received keepalive from server")
                                        else
                                            Logger.logTrace (fun () -> $"Push client: Unknown cmd 0x{cmd:X2}")
                                    | Error () ->
                                        // Invalid payload format - log and continue (spec 042: don't exit)
                                        Logger.logWarn "Push client: Invalid payload format after decryption"
                                | Error msg ->
                                    // Decryption failed - log and continue (spec 042: don't exit)
                                    Logger.logWarn $"Push client: Decryption failed: {msg}"
                        | Error e ->
                            Logger.logTrace (fun () -> $"Push client: Invalid push datagram received: '{e}'.")

                        // Log stats periodically.
                        if clientPushStats.shouldLog() then
                            Logger.logInfo (clientPushStats.getSummary())
                with
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.TimedOut ->
                    // Timeout is expected - allows checking cancellation and auth changes.
                    if clientPushStats.shouldLog() then
                        Logger.logInfo (clientPushStats.getSummary())
                | :? SocketException as ex when ex.SocketErrorCode = SocketError.Interrupted ->
                    // Socket was closed during shutdown.
                    ()
                | :? ObjectDisposedException -> ()
                | _ when clientCts.Token.IsCancellationRequested -> ()
                | ex -> Logger.logError $"Receive error: {ex.Message}"

            Logger.logInfo "Receive loop stopped."

        /// UDP send loop - sends queued packets to the server.
        /// Per spec 042: calls getAuth() on each iteration; skips if None.
        let sendLoop () =
            Logger.logInfo "Send loop started."
            while not clientCts.Token.IsCancellationRequested do
                try
                    // Get current auth snapshot
                    match getAuth() with
                    | None ->
                        // No auth available - backoff and retry
                        Thread.Sleep(NoAuthBackoffMs)
                    | Some auth ->
                        // Wait for packets with a short timeout.
                        if outboundQueue.wait(10) then
                            // Dequeue and send up to a batch of packets.
                            let mutable hasMore = true

                            while hasMore do
                                match outboundQueue.tryDequeue() with
                                | Some packet ->
                                    // Generate nonce for this packet
                                    let nonce = Guid.NewGuid()
                                    Logger.logTrace (fun () -> $"HEAVY LOG - About to send: {packet.Length} bytes, packet: {(summarizePacket packet)}.")

                                    // Build plaintext payload with command
                                    let plaintextPayload = buildPayload PushCmdData packet

                                    // Encrypt if needed using current auth's session key
                                    match encryptPayload auth.sessionAesKey plaintextPayload nonce with
                                    | Ok finalPayload ->
                                        // Check MTU after encryption
                                        if finalPayload.Length > PushMaxPayload then
                                            clientPushStats.droppedMtu.increment()
                                            Logger.logWarn $"Push client: Dropping oversized packet ({finalPayload.Length} > {PushMaxPayload})"
                                        else
                                            let datagram = buildPushDatagram auth.sessionId.toPushSessionId nonce finalPayload

                                            try
                                                udpClient.Send(datagram, datagram.Length) |> ignore
                                                clientPushStats.udpTxDatagrams.increment()
                                                clientPushStats.udpTxBytes.addInt(datagram.Length)
                                            with
                                            | ex -> Logger.logWarn $"Push client: Send failed: {ex.Message}"
                                    | Error msg ->
                                        // Encryption failed - log and continue (spec 042: don't exit)
                                        Logger.logWarn $"Push client: Encryption failed: {msg}"
                                | None -> hasMore <- false
                with
                | :? ObjectDisposedException -> ()
                | _ when clientCts.Token.IsCancellationRequested -> ()
                | ex -> Logger.logError $"Send error: {ex.Message}"

            Logger.logInfo "Send loop stopped."

        /// Keepalive loop - sends periodic keepalives to maintain NAT mapping.
        /// Per spec 042: calls getAuth() on each iteration; skips if None.
        let keepaliveLoop () =
            Logger.logInfo "Keepalive loop started."
            while not clientCts.Token.IsCancellationRequested do
                try
                    Thread.Sleep(PushKeepaliveIntervalMs)
                    if not clientCts.Token.IsCancellationRequested then
                        // Get current auth snapshot
                        match getAuth() with
                        | None ->
                            // No auth available - skip keepalive (will retry next interval)
                            ()
                        | Some auth ->
                            // Generate nonce for this packet
                            let nonce = Guid.NewGuid()

                            // Build plaintext keepalive payload
                            let plaintextPayload = buildPayload PushCmdKeepalive [||]

                            // Encrypt if needed using current auth's session key
                            match encryptPayload auth.sessionAesKey plaintextPayload nonce with
                            | Ok finalPayload ->
                                let datagram = buildPushDatagram auth.sessionId.toPushSessionId nonce finalPayload

                                try
                                    udpClient.Send(datagram, datagram.Length) |> ignore
                                    Logger.logTrace (fun () -> "Push client: Sent keepalive")
                                with
                                | ex -> Logger.logWarn $"Push client: Keepalive send failed: {ex.Message}"
                            | Error msg ->
                                // Encryption failed - log and continue (spec 042: don't exit)
                                Logger.logWarn $"Push client: Keepalive encryption failed: {msg}"
                with
                | :? ObjectDisposedException -> ()
                | _ when clientCts.Token.IsCancellationRequested -> ()
                | ex -> Logger.logError $"Keepalive error: {ex.Message}"

            Logger.logInfo "Keepalive loop stopped."

        /// Start the push dataplane loops.
        member _.start() =
            receiveTask <- Some (Task.Run(receiveLoop))
            sendTask <- Some (Task.Run(sendLoop))
            keepaliveTask <- Some (Task.Run(keepaliveLoop))
            Logger.logInfo "Started"

        /// Stop the push dataplane loops.
        member _.stop() =
            clientCts.Cancel()

            let waitTask (t: Task option) =
                match t with
                | Some task -> try task.Wait(TimeSpan.FromSeconds(2.0)) |> ignore with | _ -> ()
                | None -> ()

            waitTask receiveTask
            waitTask sendTask
            waitTask keepaliveTask

            receiveTask <- None
            sendTask <- None
            keepaliveTask <- None

            Logger.logInfo "Stopped"

        /// Set the packet injector for direct injection into the tunnel.
        member _.setPacketInjector(injector: IPacketInjector) =
            packetInjector <- Some injector

        /// Enqueue a packet for sending to the server.
        /// Returns true if enqueued, false if queue rejected (too large).
        member _.enqueueOutbound(packet: byte[]) : bool =
            clientPushStats.tunRxPackets.increment()
            clientPushStats.tunRxBytes.addInt(packet.Length)
            if outboundQueue.enqueue(packet) then
                true
            else
                clientPushStats.droppedQueueFullOutbound.increment()
                false

        /// Try to dequeue a received packet (for when the injector is not set).
        member _.tryDequeueInbound() : byte[] option =
            inboundPacketQueue.tryDequeue()

        /// Get the inbound queue for waiting.
        member _.inboundQueue = inboundPacketQueue

        /// Get stats.
        member _.stats = clientPushStats

        /// Get client ID.
        member _.clientId = vpnClientId

        interface IDisposable with
            member this.Dispose() =
                this.stop()
                udpClient.Close()
                udpClient.Dispose()
                clientCts.Dispose()


    let createVpnPushUdpClient (serviceData: VpnClientServiceData) (getAuth: unit -> VpnAuthResponse option) =
        new VpnPushUdpClient(serviceData, getAuth)
