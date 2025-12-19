# vpn_gateway_spec__031__udp_requestId_demux_and_core_constants.md

## Goal

Fix UDP tunnel correctness (response mismatches like `expected 0x82 got 0x83`) and reduce latency by **removing per-call `sendAndReceive` blocking behavior**, adding a **4‑byte requestId**, and consolidating all UDP protocol constants into a single **core module**.

**No changes to any WCF code.** Only UDP-related code and a new core module.

---

## Scope

Implement changes in:

- `C:\GitHub\Softellect\Vpn\Core\UdpProtocol.fs` **(NEW FILE)**
- `C:\GitHub\Softellect\Vpn\Client\UdpClient.fs` **(MODIFY)**
- `C:\GitHub\Softellect\Vpn\Server\UdpServer.fs` **(MODIFY)**

Do not modify other modules unless compilation requires updating a module open/import.

---

## Non-negotiable requirements

1. **Add `requestId : uint32` (4 bytes) to every UDP request and response header.**
2. **Client must have exactly one UDP receive loop** that reads all datagrams and dispatches responses to pending requests by `(requestId)`.
3. **Client must NOT call `UdpClient.Receive()` from request methods** (no per-method blocking receive).
4. **Client must implement a periodic cleanup loop** to timeout and remove pending requests that never receive a response.
5. **Server must echo the `requestId` back** in the response header unchanged.
6. **Consolidate all duplicated UDP protocol constants and header helpers** into the new core module `Softellect.Vpn.Core.UdpProtocol`.
7. Keep serialization exactly as today: `trySerialize` / `tryDeserialize` with `wcfSerializationFormat`.

---

## Protocol definition (Option A — fixed header, simplest)

### Header layout (v1)

| Field | Size | Notes |
|------|------|------|
| `msgType` | 1 byte | request/response discriminator |
| `clientId` | 16 bytes | GUID bytes (same as current approach) |
| `requestId` | 4 bytes | **uint32**, little-endian (use `BitConverter`) |

`HeaderSize = 1 + 16 + 4 = 21`

Payload follows immediately after header.

### Message types

Keep the existing byte values:

- Requests:
  - `Authenticate = 0x01uy`
  - `SendPackets = 0x02uy`
  - `ReceivePackets = 0x03uy`
- Responses:
  - `AuthenticateResponse = 0x81uy`
  - `SendPacketsResponse = 0x82uy`
  - `ReceivePacketsResponse = 0x83uy`
- Error response:
  - `ErrorResponse = 0xFFuy`

---

## New core module: `Vpn\Core\UdpProtocol.fs` (NEW)

Create a new file:

`C:\GitHub\Softellect\Vpn\Core\UdpProtocol.fs`

Namespace/module:

- `namespace Softellect.Vpn.Core`
- `module UdpProtocol =`

This module must contain **all** of the following:

1. **Message type constants** (exact values above).
2. `HeaderSize = 21` constant.
3. Helpers:
   - `buildDatagram : msgType:byte -> clientId:VpnClientId -> requestId:uint32 -> payload:byte[] -> byte[]`
   - `tryParseHeader : data:byte[] -> Result<msgType:byte * clientId:VpnClientId * requestId:uint32 * payload:byte[], unit>`
   - `buildErrorResponse : clientId:VpnClientId -> requestId:uint32 -> errorMsg:string -> byte[]`
4. A helper to convert request msgType -> expected response msgType:
   - `expectedResponseType : requestMsgType:byte -> byte`
     - 0x01 -> 0x81
     - 0x02 -> 0x82
     - 0x03 -> 0x83
     - otherwise -> 0xFF

**No duplicate constants may remain** in `UdpClient.fs` or `UdpServer.fs`. Those files must `open Softellect.Vpn.Core.UdpProtocol` and use the single source of truth.

---

## Client changes: `Vpn\Client\UdpClient.fs` (MODIFY)

### Remove the old pattern

Delete/stop using:

- any `sendAndReceive` that calls `udpClient.Receive()` inside request methods.

### Introduce a single receive-demux loop

Implement inside `VpnUdpClient`:

1. A `UdpClient` instance (keep as today).
2. A `CancellationTokenSource` owned by the client instance (e.g., `clientCts`).
3. A `ConcurrentDictionary<uint32, PendingRequest>` where `PendingRequest` includes:
   - `createdAtTicks : int64`
   - `expectedMsgType : byte`
   - `clientId : VpnClientId`
   - `tcs : TaskCompletionSource<byte[]>`

4. A background **receive loop** started once in ctor:
   - Blocks on `udpClient.Receive(&remoteEp)` in a loop.
   - Parses header via `UdpProtocol.tryParseHeader`.
   - If parse fails -> drop silently.
   - Otherwise:
     - Find pending request by requestId.
     - If found:
       - Complete it with the raw response datagram bytes.
       - Remove it from the dictionary.
     - If not found:
       - Drop silently (late/duplicate response).

### Add a cleanup loop (timeouts)

Start a background cleanup task once:

- Every `CleanupIntervalMs = 250`:
  - Scan pending dictionary.
  - If `nowTicks - createdAtTicks > timeoutTicks` then remove and complete that request as timeout.
- Use `RequestTimeoutMs = 2000` initially.
- Map timeouts to `Error (ConnectionErr ConnectionTimeoutErr)`.

### RequestId generation

- Maintain an `int32` backing field `nextRequestIdInt`.
- Use `let requestId = uint32 (Interlocked.Increment(&nextRequestIdInt))`.
- Ensure requestId never equals 0 (starting from 0 is fine because first increment returns 1).

### Implement request sending

Create a helper inside `VpnUdpClient`:

- `sendRequest : requestMsgType:byte -> clientId:VpnClientId -> payload:byte[] -> Result<byte[], VpnError>`
  - Allocate requestId.
  - Compute `expectedMsgType = UdpProtocol.expectedResponseType requestMsgType`.
  - Register pending entry (createdAtTicks, expectedMsgType, clientId, tcs).
  - Build datagram via `UdpProtocol.buildDatagram`.
  - Send via `udpClient.Send(...)`.
  - Wait for tcs completion with max wait `RequestTimeoutMs + 250` (this is a hard stop so calls don’t hang). If this wait expires, return `ConnectionErr ConnectionTimeoutErr` and ensure the pending entry is removed.

### Response parsing must check msgType and clientId (and requestId is implicitly matched)

After `sendRequest` returns raw response bytes, validate:

- header msgType == expected response type
- header clientId matches expected clientId
- if msgType == `ErrorResponse`, decode UTF8 message payload and return `Error (ConfigErr msg)` (or a better VpnError if you already have mapping; keep it simple).

### `receivePackets` request payload: long-poll knobs

Payload = serialize a record:

- `maxWaitMs : int`
- `maxPackets : int`

Fixed values for now:
- `maxWaitMs = 1000`
- `maxPackets = 256`

### Disposal / shutdown

- On `Dispose`, cancel clientCts, close udpClient, dispose it.
- Ensure background tasks exit when socket closes.

---

## Server changes: `Vpn\Server\UdpServer.fs` (MODIFY)

### Remove duplicated constants

Delete the local MsgType constants and HeaderSize. Use `open Softellect.Vpn.Core.UdpProtocol`.

### Parse header must include requestId

Replace current `parseHeader` with `UdpProtocol.tryParseHeader`.

### Echo requestId

Every response must include the same requestId from the request header.

### Implement receivePackets long-poll on server (minimal)

1. Define a record in `UdpServer.fs`:
   - `type ReceivePacketsRequest = { maxWaitMs : int; maxPackets : int }`

2. In `processReceivePackets`:
   - Deserialize payload to `ReceivePacketsRequest`.
   - Call `service.receivePackets clientId`.
   - If result is `Ok None`, then `Thread.Sleep(maxWaitMs)` and call `service.receivePackets clientId` once more.
   - Total wait must not exceed maxWaitMs.

3. Serialize the `VpnPacketsResult` and respond with `ReceivePacketsResponse`.

### Add TRACE logging for message flow (temporary)

In server receive loop:
- On receive: msgType, clientId, requestId, payloadLen, remote endpoint.
- On send: response msgType, requestId, len.

No heavy byte dumps.

---

## Validate: expected outcomes

After implementation:

1. No more client errors:
   - `Unexpected message type: expected 0x82 got 0x83`
   - `expected 0x83 got 0x82`

2. UDP tunnel tolerates concurrent `sendPackets` and `receivePackets` because responses are matched by requestId.

3. If server does not respond, client returns `ConnectionErr ConnectionTimeoutErr` within ~2 seconds and pending map is cleaned.

---

## Do NOT do

- Do NOT change any WCF implementation.
- Do NOT change NAT, PacketRouter, or ExternalGateway logic.
- Do NOT introduce alternative framing formats or protocol versions.

---

## Ask questions

If anything is unclear or compilation breaks due to missing imports/types, ask questions **before** changing any additional files outside the listed scope.
