# vpn_gateway_spec__030__udp_tunnel_client_server.md

## Goal

Implement a **UDP tunnel transport** (Protocol = `UDP_Tunnel`) as an alternative to WCF transport, **without touching any WCF code**.

Two new modules must be implemented:

- `C:\GitHub\Softellect\Vpn\Client\UdpClient.fs` (implements `IVpnClient`)
- `C:\GitHub\Softellect\Vpn\Server\UdpServer.fs` (hosts UDP server, routes requests to existing server logic)

**UDP framing MUST use the simplest approach (Option A): one tunneled IP packet per UDP datagram.**

No optional “OR” paths. Follow this spec exactly. If something required by this spec is missing in the repo, **STOP and ASK QUESTIONS** (do not improvise).

## Non-goals

- Do not optimize performance yet.
- Do not modify WCF transport code or `WcfClient.fs` / `WcfServer.fs`.
- Do not redesign auth/crypto unless explicitly required (reuse existing behavior as much as possible).

## Important constraint: Serialization MUST match the current system

WCF transport currently sends `byte[]` over `IVpnWcfService`, but those bytes are produced by existing helper(s) (e.g., `tryCommunicate`, `tryReply`) which serialize/deserialize F# values and map errors.

**UDP transport MUST reuse the same serialization primitives used by WCF**, so that the payload encoding of:

- `VpnAuthRequest` / `VpnAuthResponse`
- `VpnClientId`
- `(VpnClientId * byte[][])` or `byte[][]`
- `byte[][] option`

remains consistent and you do not invent a second incompatible encoding.

### Required action

Locate the existing serialization helpers used by WCF path:

- `Softellect.Wcf.Client.tryCommunicate`
- `Softellect.Wcf.Service.tryReply`
- any low-level serializer functions they call (often `jsonSerialize/jsonDeserialize`, or a binary+zip serializer)

Then **reuse the same functions** in UDP path.

If those functions are not accessible from `UdpClient.fs` / `UdpServer.fs`, create a **new shared helper module** (e.g., `Softellect.Vpn.Core.TransportCodec`) that simply calls the existing serializer functions (no new format).

## UDP wire protocol

### Addressing

Client uses:

- `data.serverAccessInfo.getIpAddress()`
- `data.serverAccessInfo.getServicePort()`

and sends UDP datagrams to `(serverIp, serverPort)`.

Server binds to `serverPort` on `IPAddress.Any` (or equivalent), and receives datagrams from arbitrary client endpoints.

### Message framing (datagram payload)

All datagrams are:

```
[1 byte msgType] [16 bytes clientId GUID] [payload bytes...]
```

- `msgType` is one byte:
  - `0x01` = authenticate
  - `0x02` = sendPackets
  - `0x03` = receivePackets
  - `0x81` = authenticateResponse
  - `0x82` = sendPacketsResponse
  - `0x83` = receivePacketsResponse
  - `0xFF` = errorResponse

- `clientId GUID` is `VpnClientId.value` encoded as the raw 16 bytes of the GUID (same as `Guid.ToByteArray()`).
  - For messages where the “payload” itself includes a clientId (e.g., receivePackets), the GUID here still MUST be present and MUST match the intended client.

- `payload bytes`:
  - MUST be produced/consumed by the same serializer used by WCF for the corresponding request/response types.

### Payload types by msgType

#### 0x01 authenticate

Payload = serialized `VpnAuthRequest`.

Response msgType = `0x81` with payload = serialized `VpnAuthResult` (Result<VpnAuthResponse, VpnError>).

#### 0x02 sendPackets

Payload = serialized `byte[][]` (the client already knows its id from access info; clientId is in the header).

Server calls: `IVpnService.sendPackets (clientId, packets)`

Response msgType = `0x82` with payload = serialized `VpnUnitResult`.

#### 0x03 receivePackets

Payload = empty (preferred). ClientId is taken from the header.

Server calls: `IVpnService.receivePackets clientId`

Response msgType = `0x83` with payload = serialized `VpnPacketsResult`.

#### 0xFF errorResponse

Payload = UTF-8 bytes of a short error string, max 1024 bytes.

UDP client maps this into the corresponding `VpnError` (use the same mapping style as WCF client, but do not lose the string).

## Server-side session endpoint tracking

WCF is connection-oriented; UDP is not. The server MUST know where to send responses for a given `VpnClientId`.

### Required minimal behavior

- Maintain an in-memory mapping: `VpnClientId -> IPEndPoint`
- Update it on **every** received UDP datagram: `endpointMap.[clientId] <- remoteEndpoint`
- Use this mapping to send responses back.

### Where to store this mapping

Use a private `ConcurrentDictionary<VpnClientId, IPEndPoint>` inside `UdpServer`. Do NOT modify `ClientRegistry` unless absolutely required.

## ReceivePackets semantics over UDP

Keep the same behavior as WCF:

- Client sends `receivePackets` request periodically.
- Server replies with `VpnPacketsResult` (`byte[][] option` inside Result), using the existing service implementation.
- No push model yet.

## Implementation requirements

### 1) Client: `Client/UdpClient.fs`

Create module `Softellect.Vpn.Client.UdpClient` with type:

- `type VpnUdpClient(data: VpnClientAccessInfo) = ...`
- Implements `IVpnClient`

Use `System.Net.Sockets.UdpClient` (or `Socket`) with these rules:

- Build server endpoint from `getIpAddress()` + `getServicePort()`.
- Use **a single** UDP socket for the client instance.
- Set receive timeout to **2000 ms**.
- Log at `Info` on creation (similar to `VpnWcfClient`), keep other logs minimal.

#### Call flow for each method

- `authenticate`:
  - Encode request as datagram type 0x01.
  - Send.
  - Receive response datagram.
  - Validate response type is 0x81, and the returned clientId matches.
  - Deserialize payload into `VpnAuthResult` and return.

- `sendPackets`:
  - Encode packets as datagram type 0x02 (one request for the whole `byte[][]` batch).
  - Receive response type 0x82 and deserialize to `VpnUnitResult`.

- `receivePackets`:
  - Encode request as datagram type 0x03 (payload empty).
  - Receive response type 0x83 and deserialize to `VpnPacketsResult`.

#### Safety/validation rules

- If response datagram is too short (< 1+16), return an error.
- If response msgType mismatches expected, return an error.
- If clientId in response header mismatches, return an error.
- If timeout occurs, return a `ConnectionErr` (or closest equivalent) matching existing patterns.

#### Factory function

Implement:

- `let createVpnUdpClient (clientAccessInfo: VpnClientAccessInfo) : IVpnClient = ...`

### 2) Server: `Server/UdpServer.fs`

Create module `Softellect.Vpn.Server.UdpServer`.

Implement a hosted UDP listener that can be started/stopped similarly to the WCF host infrastructure, but **without changing any WCF code**.

#### Required public shape

Implement (required):

- `getUdpHostedService : VpnServerData -> IVpnService -> IHostedService`

This returns an `IHostedService` that runs the UDP loop and delegates to the provided `IVpnService` instance.

If the current architecture does not expose an `IVpnService` instance to pass in, STOP and ASK where to hook this.

#### UDP server loop

- Bind UDP socket to `(IPAddress.Any, serverPort)` where `serverPort = data.serverAccessInfo.serviceAccessInfo.getServicePort()`.
- Run a background loop until cancellation requested.
- For each received datagram:
  - Validate framing.
  - Update endpoint map (clientId -> remoteEndPoint).
  - Dispatch based on msgType:
    - 0x01 authenticate: call `service.authenticate`
    - 0x02 sendPackets: call `service.sendPackets (clientId, packets)`
    - 0x03 receivePackets: call `service.receivePackets clientId`
  - Serialize response result using the same serializer as WCF.
  - Reply to sender endpoint with appropriate response msgType (0x81/0x82/0x83).

#### Concurrency

Process requests sequentially on the receive loop thread (no Task.Run fan-out).

#### Timeouts / shutdown

Use receive timeout **250ms** (or `ReceiveAsync` + cancellation) so stop is responsive.

#### Invalid datagrams

- If you cannot parse header, drop silently.
- If you can parse header but payload fails, reply with `0xFF` errorResponse (short UTF-8 message, <=1024 bytes).
- Do NOT log per-packet errors at high volume.

## What you must verify before coding (ask if missing)

1. The exact serializer functions used by WCF (`tryCommunicate` / `tryReply` call path).
2. Definition of `VpnUnitResult` and the `VpnError` DU (for correct error mapping).
3. Where startup chooses between `WCF_Tunnel` and `UDP_Tunnel`, and how to supply `IVpnService` instance to UDP host.

If any of these are unclear, ASK QUESTIONS before implementation.

## Acceptance criteria

- Project compiles.
- With protocol set to `UDP_Tunnel`, client can: authenticate, sendPackets, receivePackets.
- With protocol set to `WCF_Tunnel`, behavior is unchanged.
- UDP client uses `getIpAddress()` and `getServicePort()` (not URL).
- UDP framing matches exactly: 1 byte type + 16 bytes guid + payload.
