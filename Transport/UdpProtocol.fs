namespace Softellect.Transport

open System
open System.Collections.Generic
open System.Diagnostics
open System.Security.Cryptography
open System.Threading
open Softellect.Sys.Crypto

module UdpProtocol =

    type PushSessionId =
        | PushSessionId of byte
        member this.value = let (PushSessionId v) = this in v
        static member serverReserved = PushSessionId 0uy

    /// New push header layout (spec 041):
    /// sessionId (1 byte) + nonce (16 bytes) + payload
    [<Literal>]
    let PushSessionIdSize = 1

    [<Literal>]
    let PushNonceSize = 16

    /// Total header size: sessionId (1) + nonce (16) = 17 bytes
    [<Literal>]
    let PushHeaderSize = 17

    /// MTU for push dataplane (conservative to avoid fragmentation)
    [<Literal>]
    // let PushMtu = 1380
    let PushMtu = 1550
    // let PushMtu = 2900

    /// Must be smaller than or equal to PushMtu - PushHeaderSize.
    [<Literal>]
    let MtuSize = 1300

    [<Literal>]
    let CleanupIntervalMs = 250

    [<Literal>]
    let ServerReceiveTimeoutMs = 250

    [<Literal>]
    let SendBufferSize = 16 * 1024 * 1024

    [<Literal>]
    let ServerReassemblyTimeoutMs = 2000


    // /// Key for a reassembly map: (msgType, clientId, requestId).
    // type ReassemblyKey = byte * VpnClientId * uint32

    /// State for an in-progress reassembly.
    type ReassemblyState =
        {
            createdAtTicks : int64
            fragCount : uint16
            mutable receivedCount : int
            fragments : byte[][] // indexed by fragIndex
            mutable totalLength : int
        }


    /// Push command types (inside payload, first byte)
    [<Literal>]
    let PushCmdData = 1uy

    [<Literal>]
    let PushCmdKeepalive = 2uy

    [<Literal>]
    let PushCmdControl = 3uy

    /// Maximum payload per push datagram (after header)
    let PushMaxPayload = PushMtu - PushHeaderSize

    /// Keepalive interval in milliseconds
    [<Literal>]
    let PushKeepaliveIntervalMs = 10000

    /// Session freshness timeout in seconds (endpoint considered stale after this)
    [<Literal>]
    let PushSessionFreshnessSeconds = 60

    /// Queue limits for bounded packet queues
    [<Literal>]
    let PushQueueMaxBytes = 2 * 1024 * 1024  // 2 MiB

    [<Literal>]
    let PushQueueMaxPackets = 2048

    /// Stats logging interval in milliseconds
    [<Literal>]
    // let PushStatsIntervalMs = 5_000
    let PushStatsIntervalMs = 3_600_000


    // ============================================================
    // DATAGRAM FORMAT (AES-ENCRYPTED):
    // [0..16]   = nonce GUID bytes + sessionId (17 bytes)
    // [17..end] = payload bytes
    //
    // Payload format (after decryption):
    // [0]       = command byte (PushCmdData, PushCmdKeepalive, etc.)
    // [1..end]  = command-specific data
    // ============================================================


    /// Derive a per-packet AES key from session key and nonce using HMAC-SHA256.
    /// Uses domain separation: 0x01 suffix for the key, 0x02 suffix for IV.
    let derivePacketAesKey (sessionAesKey: byte[]) (nonce: Guid) : AesKey =
        let nonceBytes = nonce.ToByteArray()
        use hmac = new HMACSHA256(sessionAesKey)

        // Derive key: HMAC-SHA256(sessionAesKey, nonceBytes || 0x01)
        let keyInput = Array.append nonceBytes [| 0x01uy |]
        let keyMaterial = hmac.ComputeHash(keyInput)

        // Derive IV: HMAC-SHA256(sessionAesKey, nonceBytes || 0x02)[0..15]
        let ivInput = Array.append nonceBytes [| 0x02uy |]
        let ivMaterial = hmac.ComputeHash(ivInput)

        { key = keyMaterial; iv = ivMaterial[0..15] }


    /// Packs a byte and Guid into a 17-byte array
    let private packByteAndGuid (PushSessionId b) (g: Guid) : byte[] =
        let guidBytes = g.ToByteArray()

        // XOR of all 17 bytes
        let xorValue =
            guidBytes
            |> Array.fold (fun acc x -> acc ^^^ x) b

        let pos = int (xorValue % 17uy)

        let result = Array.zeroCreate<byte> 17

        // place the byte
        result[pos] <- b

        // fill the remaining positions with GUID bytes in order
        let mutable gi = 0
        for i = 0 to 16 do
            if i <> pos then
                result[i] <- guidBytes[gi]
                gi <- gi + 1

        result


    /// Unpacks a session id byte and Guid from the first 17 bytes of an array (payload may follow)
    let private unpackByteAndGuid (data: byte[]) =
        if data.Length < 17 then
            Error "Input array must be at least 17 bytes"
        else
            // Only the first 17 bytes participate in the scheme.
            // data[0..16] = (sessionId + 16 guid bytes, with sessionId placed at pos)
            let header = data.AsSpan(0, 17)

            // XOR of the 17 header bytes
            let mutable xorValue = 0uy
            for i = 0 to 16 do
                xorValue <- xorValue ^^^ header[i]

            let pos = int (xorValue % 17uy)

            let b = header[pos]

            // Extract exactly 16 GUID bytes from the header, skipping pos, preserving order
            let guidBytes = Array.zeroCreate<byte> 16
            let mutable gi = 0
            for i = 0 to 16 do
                if i <> pos then
                    guidBytes[gi] <- header[i]
                    gi <- gi + 1

            Ok (PushSessionId b, Guid(guidBytes))


    /// Build a push datagram with the new format (spec 041).
    /// Wire layout: sessionId (1 byte) + nonce (16 bytes) + payload
    let buildPushDatagram (sessionId: PushSessionId) (nonce: Guid) (payload: byte[]) : byte[] =
        let payloadLen = payload.Length
        if payloadLen > PushMaxPayload then
            failwithf $"Payload too large for push datagram: %d{payloadLen} > %d{PushMaxPayload}"

        let result = Array.zeroCreate (PushHeaderSize + payloadLen)
        let packedArray = packByteAndGuid sessionId nonce
        Array.Copy(packedArray, 0, result, 0, PushHeaderSize)

        // payload
        if payloadLen > 0 then
            Array.Copy(payload, 0, result, PushHeaderSize, payloadLen)

        result


    /// Try to parse a push datagram (spec 041).
    /// Returns Ok (sessionId, nonce, payloadBytes) or Error () if invalid.
    let tryParsePushDatagram (data: byte[]) =
        if data.Length < PushHeaderSize then
            Error $"data.Length: {data.Length} is too small"
        else
            match unpackByteAndGuid data with
            | Ok (sessionId, nonce) ->
                let payload =
                    if data.Length > PushHeaderSize then
                        Array.sub data PushHeaderSize (data.Length - PushHeaderSize)
                    else [||]
                Ok (sessionId, nonce, payload)
            | Error e -> Error $"%A{e}"


    /// Build a plaintext payload with a command byte prefix.
    let buildPayload (cmd: byte) (data: byte[]) : byte[] =
        let result = Array.zeroCreate (1 + data.Length)
        result[0] <- cmd
        if data.Length > 0 then
            Array.Copy(data, 0, result, 1, data.Length)
        result


    /// Try to parse plaintext payload into (command, data).
    /// Returns Ok (cmd, data) or Error () if the payload is empty.
    let tryParsePayload (payload: byte[]) : Result<byte * byte[], unit> =
        if payload.Length < 1 then
            Error ()
        else
            let cmd = payload[0]
            let data =
                if payload.Length > 1 then
                    Array.sub payload 1 (payload.Length - 1)
                else [||]
            Ok (cmd, data)


    /// A thread-safe bounded packet queue with byte and packet limits.
    /// Uses head-drop policy: when full, drops oldest packets to make room.
    type BoundedPacketQueue(maxBytes: int, maxPackets: int) =
        let queue = Queue<byte[]>()
        let lockObj = obj()
        let mutable totalBytesCount = 0
        let mutable droppedPacketsCount = 0L
        let mutable droppedBytesCount = 0L
        let signal = new ManualResetEventSlim(false)

        /// Enqueue a packet, dropping the oldest if necessary (head-drop).
        /// Returns true if packet was enqueued, false if packet was too large.
        member _.enqueue(packet: byte[]) : bool =
            if packet.Length > maxBytes then
                // Single packet exceeds max bytes - reject it
                false
            else
                lock lockObj (fun () ->
                    // Drop oldest until we have room
                    while (totalBytesCount + packet.Length > maxBytes || queue.Count >= maxPackets) && queue.Count > 0 do
                        let dropped = queue.Dequeue()
                        totalBytesCount <- totalBytesCount - dropped.Length
                        droppedPacketsCount <- droppedPacketsCount + 1L
                        droppedBytesCount <- droppedBytesCount + int64 dropped.Length

                    queue.Enqueue(packet)
                    totalBytesCount <- totalBytesCount + packet.Length
                    signal.Set()
                )
                true

        /// Try to dequeue a packet without blocking.
        member _.tryDequeue() : byte[] option =
            lock lockObj (fun () ->
                if queue.Count > 0 then
                    let packet = queue.Dequeue()
                    totalBytesCount <- totalBytesCount - packet.Length
                    if queue.Count = 0 then signal.Reset()
                    Some packet
                else
                    None
            )

        /// Dequeue up to maxCount packets without blocking.
        member _.dequeueMany(maxCount: int) : byte[][] =
            lock lockObj (fun () ->
                let count = min maxCount queue.Count
                let result = Array.zeroCreate count
                for i = 0 to count - 1 do
                    let packet = queue.Dequeue()
                    totalBytesCount <- totalBytesCount - packet.Length
                    result[i] <- packet
                if queue.Count = 0 then signal.Reset()
                result
            )

        /// Wait for packets to be available with timeout.
        /// Returns true if packets are available, false on timeout.
        member _.wait(timeoutMs: int) : bool =
            signal.Wait(timeoutMs)

        /// Get the wait handle for async wait.
        member _.waitHandle = signal.WaitHandle

        member _.count =
            lock lockObj (fun () -> queue.Count)

        member _.TotalBytes =
            lock lockObj (fun () -> totalBytesCount)

        member _.droppedPackets = Interlocked.Read(&droppedPacketsCount)
        member _.droppedBytes = Interlocked.Read(&droppedBytesCount)

        /// Reset drop counters (for stats interval reset).
        member _.resetDropCounters() =
            Interlocked.Exchange(&droppedPacketsCount, 0L) |> ignore
            Interlocked.Exchange(&droppedBytesCount, 0L) |> ignore


    /// Atomic counter for observability.
    type AtomicCounter() =
        let mutable valueCounter = 0L

        member _.increment() = Interlocked.Increment(&valueCounter) |> ignore
        member _.add(n: int64) = Interlocked.Add(&valueCounter, n) |> ignore
        member _.addInt(n: int) = Interlocked.Add(&valueCounter, int64 n) |> ignore
        member _.value = Interlocked.Read(&valueCounter)
        member _.reset() = Interlocked.Exchange(&valueCounter, 0L)


    /// Sender-side observability counters.
    type SenderPushStats() =
        let tunRxPacketsCounter = AtomicCounter()
        let tunRxBytesCounter = AtomicCounter()
        let udpTxDatagramsCounter = AtomicCounter()
        let udpTxBytesCounter = AtomicCounter()
        let udpRxDatagramsCounter = AtomicCounter()
        let udpRxBytesCounter = AtomicCounter()
        let droppedMtuCounter = AtomicCounter()
        let droppedQueueFullOutboundCounter = AtomicCounter()
        let droppedQueueFullInjectCounter = AtomicCounter()
        let mutable lastLogTicksCounter = Stopwatch.GetTimestamp()

        member _.tunRxPackets = tunRxPacketsCounter
        member _.tunRxBytes = tunRxBytesCounter
        member _.udpTxDatagrams = udpTxDatagramsCounter
        member _.udpTxBytes = udpTxBytesCounter
        member _.udpRxDatagrams = udpRxDatagramsCounter
        member _.udpRxBytes = udpRxBytesCounter
        member _.droppedMtu = droppedMtuCounter
        member _.droppedQueueFullOutbound = droppedQueueFullOutboundCounter
        member _.droppedQueueFullInject = droppedQueueFullInjectCounter

        /// Check if stats should be logged (every PushStatsIntervalMs).
        member _.shouldLog() =
            let now = Stopwatch.GetTimestamp()
            let intervalTicks = int64 PushStatsIntervalMs * Stopwatch.Frequency / 1000L
            if now - lastLogTicksCounter >= intervalTicks then
                lastLogTicksCounter <- now
                true
            else
                false

        /// Get stats summary string.
        member this.getSummary() =
            $"SENDER PUSH STATS: tun_rx=%d{tunRxPacketsCounter.value}/%d{tunRxBytesCounter.value}B " +
            $"udp_tx=%d{udpTxDatagramsCounter.value}/%d{udpTxBytesCounter.value}B " +
            $"udp_rx=%d{udpRxDatagramsCounter.value}/%d{udpRxBytesCounter.value}B " +
            $"dropped(mtu=%d{droppedMtuCounter.value} outQ=%d{droppedQueueFullOutboundCounter.value} " +
            $"injQ=%d{droppedQueueFullInjectCounter.value})"


    /// Receiver-side observability counters.
    type ReceiverPushStats() =
        let udpRxDatagramsCounter = AtomicCounter()
        let udpRxBytesCounter = AtomicCounter()
        let udpTxDatagramsCounter = AtomicCounter()
        let udpTxBytesCounter = AtomicCounter()
        let unknownClientDropsCounter = AtomicCounter()
        let noEndpointDropsCounter = AtomicCounter()
        let queueFullDropsCounter = AtomicCounter()
        let overSizeDropsCounter = AtomicCounter()
        let mutable lastLogTicks = Stopwatch.GetTimestamp()

        member _.udpRxDatagrams = udpRxDatagramsCounter
        member _.udpRxBytes = udpRxBytesCounter
        member _.udpTxDatagrams = udpTxDatagramsCounter
        member _.udpTxBytes = udpTxBytesCounter
        member _.unknownClientDrops = unknownClientDropsCounter
        member _.noEndpointDrops = noEndpointDropsCounter
        member _.queueFullDrops = queueFullDropsCounter
        member _.overSizeDrops = overSizeDropsCounter

        /// Check if stats should be logged (every PushStatsIntervalMs).
        member _.shouldLog() =
            let now = Stopwatch.GetTimestamp()
            let intervalTicks = int64 PushStatsIntervalMs * Stopwatch.Frequency / 1000L
            if now - lastLogTicks >= intervalTicks then
                lastLogTicks <- now
                true
            else
                false

        /// Get stats summary string.
        member this.getSummary() =
            $"RECEIVER PUSH STATS: udp_rx=%d{udpRxDatagramsCounter.value}/%d{udpRxBytesCounter.value}B " +
            $"udp_tx=%d{udpTxDatagramsCounter.value}/%d{udpTxBytesCounter.value}B " +
            $"dropped(unknown=%d{unknownClientDropsCounter.value} noEp=%d{noEndpointDropsCounter.value} " +
            $"qFull=%d{queueFullDropsCounter.value} mtu=%d{overSizeDropsCounter.value})"
