namespace Softellect.Vpn.Core

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open Softellect.Vpn.Core.Primitives

module UdpProtocol =

    // === Message type constants ===

    [<Literal>]
    let MsgTypeAuthenticate = 0x01uy

    [<Literal>]
    let MsgTypeSendPackets = 0x02uy

    [<Literal>]
    let MsgTypeReceivePackets = 0x03uy

    [<Literal>]
    let MsgTypeAuthenticateResponse = 0x81uy

    [<Literal>]
    let MsgTypeSendPacketsResponse = 0x82uy

    [<Literal>]
    let MsgTypeReceivePacketsResponse = 0x83uy

    [<Literal>]
    let MsgTypeErrorResponse = 0xFFuy

    // === Header layout: msgType (1) + clientId (16) + requestId (4) = 21 bytes ===

    [<Literal>]
    let HeaderSize = 21

    // === Client-side constants ===

    [<Literal>]
    let RequestTimeoutMs = 2000

    [<Literal>]
    let CleanupIntervalMs = 250

    [<Literal>]
    let DefaultMaxWaitMs = 1000

    [<Literal>]
    let DefaultMaxPackets = 256

    // === Server-side constants ===

    [<Literal>]
    let ServerReceiveTimeoutMs = 250

    [<Literal>]
    let ServerReassemblyTimeoutMs = 2000

    // === Fragmentation constants ===

    /// Maximum UDP datagram size to avoid IP fragmentation.
    [<Literal>]
    let MaxUdpDatagramSize = 1200

    /// Fragment header size: fragIndex (2 bytes) + fragCount (2 bytes).
    [<Literal>]
    let FragHeaderSize = 4

    /// Maximum payload bytes per fragment.
    [<Literal>]
    let MaxPayloadPerFragment = MaxUdpDatagramSize - HeaderSize - FragHeaderSize  // 1175


    /// Build a datagram with header: msgType (1 byte) + clientId (16 bytes) + requestId (4 bytes) + payload
    /// Note: This is the old format without fragmentation header. Use buildFragments for new code.
    let buildDatagram (msgType: byte) (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[] =
        let guidBytes = clientId.value.ToByteArray()
        let requestIdBytes = BitConverter.GetBytes(requestId) // little-endian
        let result = Array.zeroCreate (HeaderSize + payload.Length)
        result.[0] <- msgType
        Array.Copy(guidBytes, 0, result, 1, 16)
        Array.Copy(requestIdBytes, 0, result, 17, 4)
        Array.Copy(payload, 0, result, HeaderSize, payload.Length)
        result


    /// Build a single fragment datagram with full header (base + frag).
    /// Wire layout: msgType (1) + clientId (16) + requestId (4) + fragIndex (2) + fragCount (2) + fragmentPayload
    let private buildFragmentDatagram
        (msgType: byte)
        (clientId: VpnClientId)
        (requestId: uint32)
        (fragIndex: uint16)
        (fragCount: uint16)
        (fragmentPayload: byte[])
        : byte[] =
        let fullHeaderSize = HeaderSize + FragHeaderSize
        let result = Array.zeroCreate (fullHeaderSize + fragmentPayload.Length)
        let guidBytes = clientId.value.ToByteArray()
        let requestIdBytes = BitConverter.GetBytes(requestId)
        let fragIndexBytes = BitConverter.GetBytes(fragIndex)
        let fragCountBytes = BitConverter.GetBytes(fragCount)
        result.[0] <- msgType
        Array.Copy(guidBytes, 0, result, 1, 16)
        Array.Copy(requestIdBytes, 0, result, 17, 4)
        Array.Copy(fragIndexBytes, 0, result, 21, 2)
        Array.Copy(fragCountBytes, 0, result, 23, 2)
        Array.Copy(fragmentPayload, 0, result, fullHeaderSize, fragmentPayload.Length)
        result


    /// Build fragments from a logical payload.
    /// Returns an array of datagrams, each containing base header + frag header + fragment payload.
    /// For payloads that fit in a single fragment, returns a single-element array with fragIndex=0, fragCount=1.
    /// This implementation avoids per-fragment Array.sub allocations by copying directly into datagram buffers.
    let buildFragments (msgType: byte) (clientId: VpnClientId) (requestId: uint32) (payload: byte[]) : byte[][] =
        let fullHeaderSize = HeaderSize + FragHeaderSize
        let guidBytes = clientId.value.ToByteArray()
        let requestIdBytes = BitConverter.GetBytes(requestId)

        if payload.Length <= MaxPayloadPerFragment then
            // Single fragment case - build datagram directly.
            let result = Array.zeroCreate (fullHeaderSize + payload.Length)
            result.[0] <- msgType
            Array.Copy(guidBytes, 0, result, 1, 16)
            Array.Copy(requestIdBytes, 0, result, 17, 4)
            // fragIndex = 0
            result.[21] <- 0uy
            result.[22] <- 0uy
            // fragCount = 1
            result.[23] <- 1uy
            result.[24] <- 0uy
            Array.Copy(payload, 0, result, fullHeaderSize, payload.Length)
            [| result |]
        else
            // Calculate number of fragments needed.
            let fragCount = (payload.Length + MaxPayloadPerFragment - 1) / MaxPayloadPerFragment
            let fragments = Array.zeroCreate fragCount
            let fragCountBytes = BitConverter.GetBytes(uint16 fragCount)

            for i = 0 to fragCount - 1 do
                let offset = i * MaxPayloadPerFragment
                let length = min MaxPayloadPerFragment (payload.Length - offset)
                let fragIndexBytes = BitConverter.GetBytes(uint16 i)

                // Allocate datagram buffer and copy header + payload directly (no intermediate Array.sub).
                let result = Array.zeroCreate (fullHeaderSize + length)
                result.[0] <- msgType
                Array.Copy(guidBytes, 0, result, 1, 16)
                Array.Copy(requestIdBytes, 0, result, 17, 4)
                Array.Copy(fragIndexBytes, 0, result, 21, 2)
                Array.Copy(fragCountBytes, 0, result, 23, 2)
                Array.Copy(payload, offset, result, fullHeaderSize, length)
                fragments.[i] <- result

            fragments


    /// Parse a datagram header (old format without frag header).
    /// Returns Ok (msgType, clientId, requestId, payload) or Error () if header is too short.
    let tryParseHeader (data: byte[]) : Result<byte * VpnClientId * uint32 * byte[], unit> =
        if data.Length < HeaderSize then
            Error ()
        else
            let msgType = data.[0]
            let guidBytes = data.[1..16]
            let clientId = Guid(guidBytes) |> VpnClientId
            let requestId = BitConverter.ToUInt32(data, 17)
            let payload = if data.Length > HeaderSize then data.[HeaderSize..] else [||]
            Ok (msgType, clientId, requestId, payload)


    /// Parse a fragment datagram header.
    /// Returns Ok (msgType, clientId, requestId, fragIndex, fragCount, fragmentPayload) or Error () if too short.
    let tryParseFragmentHeader (data: byte[]) : Result<byte * VpnClientId * uint32 * uint16 * uint16 * byte[], unit> =
        let fullHeaderSize = HeaderSize + FragHeaderSize
        if data.Length < fullHeaderSize then
            Error ()
        else
            let msgType = data.[0]
            let guidBytes = data.[1..16]
            let clientId = Guid(guidBytes) |> VpnClientId
            let requestId = BitConverter.ToUInt32(data, 17)
            let fragIndex = BitConverter.ToUInt16(data, 21)
            let fragCount = BitConverter.ToUInt16(data, 23)
            let fragmentPayload =
                if data.Length > fullHeaderSize then
                    Array.sub data fullHeaderSize (data.Length - fullHeaderSize)
                else
                    [||]
            Ok (msgType, clientId, requestId, fragIndex, fragCount, fragmentPayload)


    /// Build an error response as fragmented datagrams.
    let buildErrorResponseFragments (clientId: VpnClientId) (requestId: uint32) (errorMsg: string) : byte[][] =
        let msgBytes = System.Text.Encoding.UTF8.GetBytes(errorMsg)
        let truncated = if msgBytes.Length > 1024 then msgBytes.[..1023] else msgBytes
        buildFragments MsgTypeErrorResponse clientId requestId truncated


    /// Map request msgType to expected response msgType.
    let expectedResponseType (requestMsgType: byte) : byte =
        match requestMsgType with
        | 0x01uy -> MsgTypeAuthenticateResponse
        | 0x02uy -> MsgTypeSendPacketsResponse
        | 0x03uy -> MsgTypeReceivePacketsResponse
        | _ -> MsgTypeErrorResponse


    /// Record for receivePackets long-poll request payload.
    type ReceivePacketsRequest =
        {
            maxWaitMs : int
            maxPackets : int
        }


    // === Reassembly types and logic ===

    /// Key for reassembly map: (msgType, clientId, requestId).
    type ReassemblyKey = byte * VpnClientId * uint32

    /// State for an in-progress reassembly.
    type ReassemblyState =
        {
            createdAtTicks : int64
            fragCount : uint16
            mutable receivedCount : int
            fragments : byte[][] // indexed by fragIndex
            mutable totalLength : int
        }

    /// Create a new reassembly state for the given fragCount.
    let createReassemblyState (createdAtTicks: int64) (fragCount: uint16) : ReassemblyState =
        {
            createdAtTicks = createdAtTicks
            fragCount = fragCount
            receivedCount = 0
            fragments = Array.zeroCreate (int fragCount)
            totalLength = 0
        }

    /// Try to add a fragment to the reassembly state.
    /// Returns Some(logicalPayload) if reassembly is complete, None otherwise.
    let tryAddFragment (state: ReassemblyState) (fragIndex: uint16) (fragmentPayload: byte[]) : byte[] option =
        let idx = int fragIndex
        if idx < 0 || idx >= state.fragments.Length then
            None // Invalid index - ignore.
        elif state.fragments.[idx] <> null then
            // Duplicate fragment - ignore but check if already complete.
            if state.receivedCount = int state.fragCount then
                // Already complete - reassemble again.
                let result = Array.zeroCreate state.totalLength
                let mutable offset = 0
                for i = 0 to state.fragments.Length - 1 do
                    let frag = state.fragments.[i]
                    Array.Copy(frag, 0, result, offset, frag.Length)
                    offset <- offset + frag.Length
                Some result
            else
                None
        else
            // New fragment.
            state.fragments.[idx] <- fragmentPayload
            state.receivedCount <- state.receivedCount + 1
            state.totalLength <- state.totalLength + fragmentPayload.Length

            if state.receivedCount = int state.fragCount then
                // All fragments received - reassemble.
                let result = Array.zeroCreate state.totalLength
                let mutable offset = 0
                for i = 0 to state.fragments.Length - 1 do
                    let frag = state.fragments.[i]
                    Array.Copy(frag, 0, result, offset, frag.Length)
                    offset <- offset + frag.Length
                Some result
            else
                None


    // ==========================================================================
    // PUSH DATAPLANE PROTOCOL (spec 037)
    // ==========================================================================

    /// Magic number for push dataplane: "VPN1" = 0x56504E31
    [<Literal>]
    let PushMagic = 0x56504E31u

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
    /// magic (4) + version (1) + msgType (1) + flags (2) + clientId (16) + seq (4) + payloadLen (2) + reserved (2) = 32 bytes
    [<Literal>]
    let PushHeaderSize = 32

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

        // magic (4 bytes, big-endian for readability in wireshark)
        result.[0] <- byte ((PushMagic >>> 24) &&& 0xFFu)
        result.[1] <- byte ((PushMagic >>> 16) &&& 0xFFu)
        result.[2] <- byte ((PushMagic >>> 8) &&& 0xFFu)
        result.[3] <- byte (PushMagic &&& 0xFFu)

        // version (1 byte)
        result.[4] <- PushVersion

        // msgType (1 byte)
        result.[5] <- msgType

        // flags (2 bytes) - reserved, set to 0
        result.[6] <- 0uy
        result.[7] <- 0uy

        // clientId (16 bytes)
        Array.Copy(guidBytes, 0, result, 8, 16)

        // seq (4 bytes, little-endian)
        let seqBytes = BitConverter.GetBytes(seq)
        Array.Copy(seqBytes, 0, result, 24, 4)

        // payloadLen (2 bytes, little-endian)
        let payloadLenBytes = BitConverter.GetBytes(uint16 payloadLen)
        Array.Copy(payloadLenBytes, 0, result, 28, 2)

        // reserved (2 bytes)
        result.[30] <- 0uy
        result.[31] <- 0uy

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
            magic : uint32
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
            // Parse magic (big-endian)
            let magic =
                (uint32 data.[0] <<< 24) |||
                (uint32 data.[1] <<< 16) |||
                (uint32 data.[2] <<< 8) |||
                (uint32 data.[3])

            if magic <> PushMagic then
                Error ()
            else
                let version = data.[4]
                if version <> PushVersion then
                    Error ()
                else
                    let msgType = data.[5]
                    let flags = BitConverter.ToUInt16(data, 6)
                    let guidBytes = data.[8..23]
                    let clientId = Guid(guidBytes) |> VpnClientId
                    let seq = BitConverter.ToUInt32(data, 24)
                    let payloadLen = BitConverter.ToUInt16(data, 28)

                    let expectedLen = PushHeaderSize + int payloadLen
                    if data.Length < expectedLen then
                        Error ()
                    else
                        let payload =
                            if payloadLen > 0us then
                                Array.sub data PushHeaderSize (int payloadLen)
                            else
                                [||]

                        let header =
                            {
                                magic = magic
                                version = version
                                msgType = msgType
                                flags = flags
                                clientId = clientId
                                seq = seq
                                payloadLen = payloadLen
                            }

                        Ok (header, payload)


    // ==========================================================================
    // BOUNDED PACKET QUEUE (spec 037)
    // ==========================================================================

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


    // ==========================================================================
    // OBSERVABILITY COUNTERS (spec 037)
    // ==========================================================================

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
