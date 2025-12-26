# vpn_gateway_spec__033__udp_fragmentation_and_longpoll_latency.md

## Goal

Fix UDP tunnel reliability/performance issues where:

- ICMP ping works but latency is ~1.1–1.3s (too high).
- HTTPS stalls and eventually fails.
- Client frequently times out sending/receiving (`ConnectionTimeoutErr`).
- Server logs `UDP receive error: ... datagram ... larger than the internal message buffer ... or the buffer ... smaller than the datagram itself.`

This spec is **authoritative**: implement exactly what is described, no alternative designs.

## Observed symptoms (must be addressed)

- Client warnings/errors (many):
  - `Failed to send ... ConnectionErr ConnectionTimeoutErr`
  - `Failed to receive packets: ConnectionErr ConnectionTimeoutErr`
- Server errors:
  - `UDP receive error: A message sent on a datagram socket was larger than the internal message buffer ...`
- Ping RTT ≈ 1183ms average: implies the tunnel’s “receive path” has ≈ 1 second polling delay, not raw network RTT.

## Root causes (to fix)

1) **Oversized UDP application datagrams**
- Current design serializes potentially large payloads (notably `sendPackets` and `receivePackets`) into a single UDP datagram (`buildDatagram` + payload).
- Under real traffic (HTTPS/TCP), a batch can exceed UDP practical limits or path MTU, causing drops/fragment loss, and on the receiver side can cause `MessageSize` errors.
- Result: client waits for a response that never arrives → timeouts.

2) **Long-poll implementation adds ~1s latency**
- Server `processReceivePackets` does `Thread.Sleep(maxWaitMs)` (default 1000ms) then checks once more.
- This directly creates ~1s RTT inflation for any request that hits the empty case.

## Required changes

### A) Add protocol-level fragmentation + reassembly for UDP payloads

Implement application-layer fragmentation so **no single UDP datagram** exceeds a conservative safe payload size.

#### A.1 Constants (UdpProtocol.fs)

Add these constants (exact names):

- `MaxUdpDatagramSize = 1200` (bytes)  
  Rationale: safe-ish across typical paths; avoids IP fragmentation in most cases.

- `FragHeaderSize = 4` (bytes) representing:
  - `fragIndex : uint16` (0-based)
  - `fragCount : uint16` (total fragments)

- `MaxPayloadPerFragment = MaxUdpDatagramSize - HeaderSize - FragHeaderSize`

#### A.2 Fragmented datagram format

For **every** UDP datagram sent by this tunnel (requests and responses), the payload carried on the wire must be:

`[fragIndex:uint16][fragCount:uint16][fragmentPayload:byte[]]`

The existing outer header remains unchanged:
`msgType (1) + clientId (16) + requestId (4)`

So the wire layout becomes:

- 0: `msgType` (1 byte)
- 1..16: `clientId` (16 bytes)
- 17..20: `requestId` (4 bytes)
- 21..22: `fragIndex` (2 bytes, little-endian)
- 23..24: `fragCount` (2 bytes, little-endian)
- 25..: fragment payload bytes

**Non-fragmented messages are still sent using this format** with:
- `fragIndex = 0`
- `fragCount = 1`

This avoids “mixed format” parsing.

#### A.3 UdpProtocol helpers (UdpProtocol.fs)

Add functions (exact behaviors required):

1) `buildFragments : byte -> VpnClientId -> uint32 -> byte[] -> byte[][]`  
- Input is the logical payload (the serialized payload for the request/response).
- Output is an array of datagrams (each is a full UDP packet including base header + frag header + fragment bytes).
- Must split payload into chunks of size `<= MaxPayloadPerFragment`.
- Must set `fragCount` correctly for all fragments.
- Must set `fragIndex` correctly (0..fragCount-1).

2) `tryParseFragmentHeader : byte[] -> Result<byte * VpnClientId * uint32 * uint16 * uint16 * byte[], unit>`  
- Validates length is at least `HeaderSize + FragHeaderSize`.
- Returns: `(msgType, clientId, requestId, fragIndex, fragCount, fragmentPayload)`.
- Must NOT allocate large temporary arrays unnecessarily beyond extracting the fragment payload slice.

#### A.4 Reassembly logic

Reassembly is required on both client and server for incoming UDP datagrams.

##### A.4.1 Reassembly key

Key fragments by tuple:
- `(msgType, clientId, requestId)`

Remote endpoint is **not** part of the key. (Client uses a connected socket; server uses `endpointMap` for responses but requests include clientId, so keying is sufficient.)

##### A.4.2 Reassembly state

Maintain a concurrent map of in-progress reassemblies containing:

- `createdAtTicks : int64`
- `fragCount : uint16`
- `receivedCount : int`
- storage for fragments by `fragIndex` (e.g., `byte[][]` length = fragCount)
- total assembled length (sum of fragment lengths), tracked incrementally

##### A.4.3 Reassembly timeout

Use the same timeout window as request cleanup:
- timeout ticks computed from `RequestTimeoutMs` (client) and a new server constant:
  - Add `ServerReassemblyTimeoutMs = 2000` in UdpProtocol.fs for server-side fragment cleanup.

Periodically cleanup stale reassemblies in both client and server (reuse existing loops or add a dedicated lightweight loop on server).

##### A.4.4 Completion

When all fragments are received (`receivedCount == fragCount`):
- Reassemble them in order into the **logical payload** (concatenation of fragment payloads in fragIndex order).
- Then process exactly as if the original “unfragmented” payload was received.

Important: deliver to higher level as **logical payload**, not as raw “wire payload”.

### B) Update client send/receive to use fragmentation

File: `C:\GitHub\Softellect\Vpn\Client\UdpClient.fs`

#### B.1 Sending
- In `sendRequest`, replace `buildDatagram ... payload` with `buildFragments ... payload`.
- Send **all fragments** sequentially via `udpClient.Send(...)`.
- If sending any fragment throws, remove the pending request and return `ConnectionErr (ServerUnreachableErr ...)`.

#### B.2 Receiving
- Replace `tryParseHeader` usage in `receiveLoop` with `tryParseFragmentHeader`.
- Feed fragments into the reassembly map.
- Only once reassembled, complete the pending request’s TCS with a reconstructed “response blob” that `parseResponse` can validate.

Implementation requirement:
- Change `parseResponse` to accept **logical payload** directly (recommended) OR reconstruct a faux “single datagram” buffer for compatibility.
- Do **not** keep both formats. Choose one and update call sites consistently.
- Preferred approach (required): update parsing pipeline so `parseResponse` validates `(msgType, clientId)` and returns the logical payload without ever needing the original raw UDP buffer.

#### B.3 Validation on completion
When the pending request is completed, validate:
- response msgType equals expected response msgType (existing logic)
- clientId matches expected
If mismatch: complete the request with `ConfigErr ...` as today.

### C) Update server receive/send to use fragmentation

File: `C:\GitHub\Softellect\Vpn\Server\UdpServer.fs`

#### C.1 Receiving
- Replace `tryParseHeader` with `tryParseFragmentHeader`.
- Reassemble fragments to obtain `(msgType, clientId, requestId, logicalPayload)`.
- Only after full reassembly:
  - update `endpointMap[clientId] <- remoteEp`
  - call `processRequest msgType clientId requestId logicalPayload`
  - send response using fragmentation (below)

#### C.2 Sending responses
- Replace `buildDatagram ... responsePayload` with `buildFragments ... responsePayload`.
- Send all response fragments to `remoteEp`.

#### C.3 Handle MessageSize errors explicitly
In `receiveLoop` exception handler:
- Add a specific catch for `SocketException` with `SocketError.MessageSize` and log it as a **WARN** with enough context to confirm fragmentation is working (e.g., “MessageSize: inbound datagram too large — verify client fragmentation; dropping.”).

After fragmentation is implemented correctly, this should disappear in normal operation.

### D) Fix long-poll latency on receivePackets (server)

File: `C:\GitHub\Softellect\Vpn\Server\UdpServer.fs`

Replace this behavior:

- current: if `Ok None` then `Thread.Sleep(maxWaitMs)` then check once

with the required behavior:

- Implement a wait loop that:
  - Tracks elapsed time with `Stopwatch`.
  - Repeatedly calls `service.receivePackets clientId`.
  - If returns `Ok (Some _)` or `Error _` → return immediately.
  - If returns `Ok None`:
    - sleep `PollSleepMs = 10` (literal 10ms)
    - continue until elapsed >= `maxWaitMs`
  - If timeout reached → return `Ok None`.

This change must reduce ping RTT inflation dramatically (should no longer be “stuck” at ~1000ms due to polling).

### E) Update UdpProtocol parsing helpers used everywhere

File: `C:\GitHub\Softellect\Vpn\Core\UdpProtocol.fs`

- Keep existing message type constants unchanged.
- Update/extend parsing/building helpers per section A.
- Any obsolete helper that is no longer used after migration can remain, but the client/server must use the new fragment-aware path.

## Acceptance criteria

1) **No server log entries** of the form:
   - `UDP receive error: ... larger than the internal message buffer ...`
   during sustained browsing/HTTPS tests.

2) **HTTPS works reliably**
- Visiting several HTTPS sites should load normally and not stall/die due to tunnel timeouts.

3) **Ping RTT improves materially**
- Ping through the tunnel should no longer be dominated by ~1000ms polling.
- Expectation: RTT should be in the “network RTT ballpark” (not an extra +1000ms).

4) **No burst of client timeouts under normal use**
- Client logs should not continuously show `ConnectionTimeoutErr` during ordinary browsing.

## Implementation constraints

- Modify only these files unless strictly necessary:
  - `C:\GitHub\Softellect\Vpn\Core\UdpProtocol.fs`
  - `C:\GitHub\Softellect\Vpn\Client\UdpClient.fs`
  - `C:\GitHub\Softellect\Vpn\Server\UdpServer.fs`
- No unrelated refactors, no stylistic changes outside the touched code paths.
- Keep logging minimal but sufficient to validate fragmentation/reassembly during initial testing.

## Notes for testing

- Re-test:
  - `ping 8.8.8.8` (observe RTT)
  - Browse to a few HTTPS sites (e.g., large pages)
  - Optional: download a moderately sized file to generate sustained TCP traffic
- If any packet loss occurs, reassembly timeout must not leak memory (cleanup must remove old partial assemblies).
