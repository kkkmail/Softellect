namespace Softellect.Vpn.Core

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open Softellect.Vpn.Core.Primitives

module UdpProtocol =

    [<Literal>]
    let CleanupIntervalMs = 250

    [<Literal>]
    let ServerReceiveTimeoutMs = 250

    [<Literal>]
    let ServerReassemblyTimeoutMs = 2000

    // /// Key for reassembly map: (msgType, clientId, requestId).
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


    /// Push protocol version
    [<Literal>]
    let PushVersion = 1uy

    /// Push message types
    [<Literal>]
    let PushMsgTypeData = 1uy

    [<Literal>]
    let PushMsgTypeKeepalive = 2uy

    [<Literal>]
    let PushMsgTypeControl = 3uy

    /// Push header layout:
    /// version (1) + msgType (1) + flags (2) + clientId (16) + seq (4) + payloadLen (2) + reserved (2) = 28 bytes
    [<Literal>]
    let PushHeaderSize = 28

    /// MTU for push dataplane (conservative to avoid fragmentation)
    [<Literal>]
    let PushMtu = 1380

    /// Maximum payload per push datagram
    let PushMaxPayload = PushMtu - PushHeaderSize  // 1348

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
    let PushStatsIntervalMs = 5000


    /// Build a push dataplane datagram.
    /// Wire layout: magic (4) + version (1) + msgType (1) + flags (2) + clientId (16) + seq (4) + payloadLen (2) + reserved (2) + payload
    let buildPushDatagram (msgType: byte) (clientId: VpnClientId) (seq: uint32) (payload: byte[]) : byte[] =
        let payloadLen = payload.Length
        if payloadLen > PushMaxPayload then
            failwithf $"Payload too large for push datagram: %d{payloadLen} > %d{PushMaxPayload}"

        let result = Array.zeroCreate (PushHeaderSize + payloadLen)
        let guidBytes = clientId.value.ToByteArray()

        // version (1 byte)
        result.[0] <- PushVersion

        // msgType (1 byte)
        result.[1] <- msgType

        // flags (2 bytes) - reserved, set to 0
        result.[2] <- 0uy
        result.[3] <- 0uy

        // clientId (16 bytes)
        Array.Copy(guidBytes, 0, result, 4, 16)

        // seq (4 bytes, little-endian)
        let seqBytes = BitConverter.GetBytes(seq)
        Array.Copy(seqBytes, 0, result, 20, 4)

        // payloadLen (2 bytes, little-endian)
        let payloadLenBytes = BitConverter.GetBytes(uint16 payloadLen)
        Array.Copy(payloadLenBytes, 0, result, 24, 2)

        // reserved (2 bytes)
        result.[26] <- 0uy
        result.[27] <- 0uy

        // payload
        if payloadLen > 0 then
            Array.Copy(payload, 0, result, PushHeaderSize, payloadLen)

        result


    /// Build a push keepalive datagram (no payload).
    let buildPushKeepalive (clientId: VpnClientId) (seq: uint32) : byte[] =
        buildPushDatagram PushMsgTypeKeepalive clientId seq [||]


    /// Build a push data datagram.
    let buildPushData (clientId: VpnClientId) (seq: uint32) (payload: byte[]) : byte[] =
        buildPushDatagram PushMsgTypeData clientId seq payload


    /// Parsed push header
    type PushHeader =
        {
            version : byte
            msgType : byte
            flags : uint16
            clientId : VpnClientId
            seq : uint32
            payloadLen : uint16
        }


    /// Try to parse a push datagram header.
    /// Returns Ok (header, payload) or Error () if invalid.
    let tryParsePushHeader (data: byte[]) : Result<PushHeader * byte[], unit> =
        if data.Length < PushHeaderSize then
            Error ()
        else
            let version = data.[0]
            if version <> PushVersion then
                Error ()
            else
                let msgType = data.[1]
                let flags = BitConverter.ToUInt16(data, 2)
                let guidBytes = data.[4..19]
                let clientId = Guid(guidBytes) |> VpnClientId
                let seq = BitConverter.ToUInt32(data, 20)
                let payloadLen = BitConverter.ToUInt16(data, 24)

                let expectedLen = PushHeaderSize + int payloadLen
                if data.Length < expectedLen then
                    Error ()
                else
                    let payload =
                        if payloadLen > 0us then
                            Array.sub data PushHeaderSize (int payloadLen)
                        else [||]

                    let header =
                        {
                            version = version
                            msgType = msgType
                            flags = flags
                            clientId = clientId
                            seq = seq
                            payloadLen = payloadLen
                        }

                    Ok (header, payload)


    /// A thread-safe bounded packet queue with byte and packet limits.
    /// Uses head-drop policy: when full, drops oldest packets to make room.
    type BoundedPacketQueue(maxBytes: int, maxPackets: int) =
        let queue = Queue<byte[]>()
        let lockObj = obj()
        let mutable totalBytes = 0
        let mutable droppedPackets = 0L
        let mutable droppedBytes = 0L
        let signal = new ManualResetEventSlim(false)

        /// Enqueue a packet, dropping oldest if necessary (head-drop).
        /// Returns true if packet was enqueued, false if packet was too large.
        member _.Enqueue(packet: byte[]) : bool =
            if packet.Length > maxBytes then
                // Single packet exceeds max bytes - reject it
                false
            else
                lock lockObj (fun () ->
                    // Drop oldest until we have room
                    while (totalBytes + packet.Length > maxBytes || queue.Count >= maxPackets) && queue.Count > 0 do
                        let dropped = queue.Dequeue()
                        totalBytes <- totalBytes - dropped.Length
                        droppedPackets <- droppedPackets + 1L
                        droppedBytes <- droppedBytes + int64 dropped.Length

                    queue.Enqueue(packet)
                    totalBytes <- totalBytes + packet.Length
                    signal.Set()
                )
                true

        /// Try to dequeue a packet without blocking.
        member _.TryDequeue() : byte[] option =
            lock lockObj (fun () ->
                if queue.Count > 0 then
                    let packet = queue.Dequeue()
                    totalBytes <- totalBytes - packet.Length
                    if queue.Count = 0 then signal.Reset()
                    Some packet
                else
                    None
            )

        /// Dequeue up to maxCount packets without blocking.
        member _.DequeueMany(maxCount: int) : byte[][] =
            lock lockObj (fun () ->
                let count = min maxCount queue.Count
                let result = Array.zeroCreate count
                for i = 0 to count - 1 do
                    let packet = queue.Dequeue()
                    totalBytes <- totalBytes - packet.Length
                    result.[i] <- packet
                if queue.Count = 0 then signal.Reset()
                result
            )

        /// Wait for packets to be available with timeout.
        /// Returns true if packets are available, false on timeout.
        member _.Wait(timeoutMs: int) : bool =
            signal.Wait(timeoutMs)

        /// Get the wait handle for async wait.
        member _.WaitHandle = signal.WaitHandle

        member _.Count =
            lock lockObj (fun () -> queue.Count)

        member _.TotalBytes =
            lock lockObj (fun () -> totalBytes)

        member _.DroppedPackets = Interlocked.Read(&droppedPackets)
        member _.DroppedBytes = Interlocked.Read(&droppedBytes)

        /// Reset drop counters (for stats interval reset).
        member _.ResetDropCounters() =
            Interlocked.Exchange(&droppedPackets, 0L) |> ignore
            Interlocked.Exchange(&droppedBytes, 0L) |> ignore


    /// Atomic counter for observability.
    type AtomicCounter() =
        let mutable value = 0L

        member _.Increment() = Interlocked.Increment(&value) |> ignore
        member _.Add(n: int64) = Interlocked.Add(&value, n) |> ignore
        member _.AddInt(n: int) = Interlocked.Add(&value, int64 n) |> ignore
        member _.Value = Interlocked.Read(&value)
        member _.Reset() = Interlocked.Exchange(&value, 0L)


    /// Client-side observability counters.
    type ClientPushStats() =
        let tunRxPackets = AtomicCounter()
        let tunRxBytes = AtomicCounter()
        let udpTxDatagrams = AtomicCounter()
        let udpTxBytes = AtomicCounter()
        let udpRxDatagrams = AtomicCounter()
        let udpRxBytes = AtomicCounter()
        let droppedMtu = AtomicCounter()
        let droppedQueueFullOutbound = AtomicCounter()
        let droppedQueueFullInject = AtomicCounter()
        let mutable lastLogTicks = Stopwatch.GetTimestamp()

        member _.TunRxPackets = tunRxPackets
        member _.TunRxBytes = tunRxBytes
        member _.UdpTxDatagrams = udpTxDatagrams
        member _.UdpTxBytes = udpTxBytes
        member _.UdpRxDatagrams = udpRxDatagrams
        member _.UdpRxBytes = udpRxBytes
        member _.DroppedMtu = droppedMtu
        member _.DroppedQueueFullOutbound = droppedQueueFullOutbound
        member _.DroppedQueueFullInject = droppedQueueFullInject

        /// Check if stats should be logged (every PushStatsIntervalMs).
        member _.ShouldLog() =
            let now = Stopwatch.GetTimestamp()
            let intervalTicks = int64 PushStatsIntervalMs * Stopwatch.Frequency / 1000L
            if now - lastLogTicks >= intervalTicks then
                lastLogTicks <- now
                true
            else
                false

        /// Get stats summary string.
        member this.GetSummary() =
            sprintf "CLIENT PUSH STATS: tun_rx=%d/%dB udp_tx=%d/%dB udp_rx=%d/%dB dropped(mtu=%d outQ=%d injQ=%d)"
                tunRxPackets.Value tunRxBytes.Value
                udpTxDatagrams.Value udpTxBytes.Value
                udpRxDatagrams.Value udpRxBytes.Value
                droppedMtu.Value droppedQueueFullOutbound.Value droppedQueueFullInject.Value


    /// Server-side observability counters.
    type ServerPushStats() =
        let udpRxDatagrams = AtomicCounter()
        let udpRxBytes = AtomicCounter()
        let udpTxDatagrams = AtomicCounter()
        let udpTxBytes = AtomicCounter()
        let unknownClientDrops = AtomicCounter()
        let noEndpointDrops = AtomicCounter()
        let queueFullDrops = AtomicCounter()
        let mutable lastLogTicks = Stopwatch.GetTimestamp()

        member _.UdpRxDatagrams = udpRxDatagrams
        member _.UdpRxBytes = udpRxBytes
        member _.UdpTxDatagrams = udpTxDatagrams
        member _.UdpTxBytes = udpTxBytes
        member _.UnknownClientDrops = unknownClientDrops
        member _.NoEndpointDrops = noEndpointDrops
        member _.QueueFullDrops = queueFullDrops

        /// Check if stats should be logged (every PushStatsIntervalMs).
        member _.ShouldLog() =
            let now = Stopwatch.GetTimestamp()
            let intervalTicks = int64 PushStatsIntervalMs * Stopwatch.Frequency / 1000L
            if now - lastLogTicks >= intervalTicks then
                lastLogTicks <- now
                true
            else
                false

        /// Get stats summary string.
        member this.GetSummary() =
            $"SERVER PUSH STATS: udp_rx=%d{udpRxDatagrams.Value}/%d{udpRxBytes.Value}B udp_tx=%d{udpTxDatagrams.Value}/%d{udpTxBytes.Value}B dropped(unknown=%d{unknownClientDrops.Value} noEp=%d{noEndpointDrops.Value} qFull=%d{queueFullDrops.Value})"
