# VPN Gateway Milestone 001 — WCF → UDP tunnel pivot, server burn fix, and UDP protocol stabilization

Date context: **2025-12-19** (America/New_York)

This file summarizes the key decisions, observations, and work items from the (very long) chat, so we can continue in a fresh thread without losing context.

## 1) Problem A: Server “hot” / high CPU burn (receive thread)

### Observation
The server was consuming significant CPU even when traffic volume did not seem insane, and later even when **no client was connected**.

Logs of interest (examples):
- `PacketRouter recv: ... wait=01.xx drain=02.xx proc=95.xx | ... rx=57,715,816`
- Later the server became “cold”:
  - `PacketRouter recv: ... wait=99.2019 drain=00.0196 proc=00.7670 | ... rx=8192`

### Code area
- `C:\GitHub\Softellect\Vpn\Server\PacketRouter.fs`
- Specifically: the WinTun `readEvent` receive loop and `drainPackets` logic.

### Root issue (hypothesis we acted on)
`drainPackets` could drain an unbounded number of packets per wakeup, with no cap / yield and weak cancellation responsiveness. Under certain conditions (stuck signaled read event or high packet availability), the loop could monopolize a thread.

### Mitigation direction
Add a **cap per wakeup** and yield behavior (throttle fairness), plus periodic cancel checks while draining.

## 2) WCF performance suspicion → explore alternatives

### Architecture recap
Client-facing abstraction:
- `IVpnClient`:
  - `authenticate : VpnAuthRequest -> VpnAuthResult`
  - `sendPackets : byte[][] -> VpnUnitResult`
  - `receivePackets : VpnClientId -> VpnPacketsResult`

Transport layer (current WCF transport contract):
- `IVpnWcfService` (WCF operations all `byte[] -> byte[]`)

Server abstraction:
- `IVpnService` provides typed methods used by the transport.

### Decision
Start a UDP tunnel transport as a replacement for WCF transport:
- Keep the abstraction surface (`IVpnClient` / `IVpnService`) stable.
- Replace the transport implementation (WCF ↔ UDP) under the hood.

## 3) UDP tunnel implementation plan and key constraints

### Files / module split
Client side:
- Implement: `C:\GitHub\Softellect\Vpn\Client\UdpClient.fs`
- Keep existing: `C:\GitHub\Softellect\Vpn\Client\WcfClient.fs`
- `VpnUdpClient` must use:
  - `data.serverAccessInfo.getIpAddress()`
  - `data.serverAccessInfo.getServicePort()`

Server side:
- Implement: `C:\GitHub\Softellect\Vpn\Server\UdpServer.fs`
- Keep existing: `C:\GitHub\Softellect\Vpn\Server\WcfServer.fs` unchanged.
- UDP server must be hosted without touching WCF hosting code. New behavior lives in UDP server module(s).

### UDP framing decision
Use the simplest framing (“Option A”):
- A small binary header + payload.

## 4) UDP tunnel issues encountered and the incremental fixes

### 4.1 Firewall
Symptom:
- Client timed out on authenticate.
- Server “never sees” traffic on UDP port.

Root cause:
- Windows Firewall rule not allowing UDP on the server.

After allowing UDP:
- Client authenticated and ping worked.

### 4.2 Response demultiplexing bug (msg types swapped)
Symptom on client:
- `Unexpected message type: expected 0x82, got 0x83`
- and the reverse on receive.

Cause:
- UDP is unordered and responses can arrive in any order; a single “sendAndReceive” pattern + shared socket can mix responses between request types (sendPackets vs receivePackets).

### 4.3 Protocol hardcoding / duplication
CC hardcoded message type constants twice (client and server).

Desired correction:
- Consolidate UDP protocol constants into a shared core module:
  - `Softellect.Vpn.Core.UdpProtocol`

### 4.4 Introduced requestId-based demux
Fix direction:
- Add `requestId : uint32` (4 bytes) into UDP header.
- Client maintains a `pendingRequests` map keyed by requestId.
- Receive loop dispatches incoming datagrams to the corresponding pending request.
- Add periodic cleanup loop to fail/evict timed-out pending requests.

New shared module:
- `C:\GitHub\Softellect\Vpn\Core\UdpProtocol.fs`
  - Header: `msgType (1) + clientId (16) + requestId (4)` = 21 bytes
  - Helpers: `buildDatagram`, `tryParseHeader`, `expectedResponseType`, `buildErrorResponse`
  - `ReceivePacketsRequest` payload struct for long-poll-ish receive semantics.

## 5) New breakage after requestId changes (client Receive requires Bind/Connect)

Symptom:
- Client log at startup:
  - `VpnUdpClient receive error: You must call the Bind method before performing this operation.`

Root cause:
- `UdpClient.Receive()` on Windows can require that the socket is **Bind**-ed (or **Connect**-ed) first.
- Current client creates `new UdpClient()` and starts `receiveLoop` immediately, calling `Receive` without binding.

Fix direction (very surgical):
- In `UdpClient.fs`, **bind** to a local ephemeral port (`IPEndPoint(IPAddress.Any, 0)`) *or* call `udpClient.Connect(serverEndpoint)` **before** starting the receive loop.
- Ensure receive loop uses a proper `remoteEp` (often `IPEndPoint(IPAddress.Any, 0)`), not the server endpoint, when calling `Receive(&remoteEp)`.

A dedicated CC instruction was prepared for this (“NNN=032”) focusing only on the bind/connect fix.

## 6) Separate recurring server-side error: ExternalGateway.sendOutbound permission

Server log symptom (example):
- `ExternalGateway.sendOutbound: Failed to send packet ... exception: 'An attempt was made to access a socket in a way forbidden by its access permissions.'`

Context:
- Happens when trying to send raw IPv4 packets via raw socket from `ExternalInterface.fs`.
- Likely permissions / Windows raw socket constraints (admin rights, WFP, local security policy) or an invalid destination (broadcast etc.), but this was not fully debugged in this thread.

File:
- `C:\GitHub\Softellect\Vpn\Server\ExternalInterface.fs`

## 7) Performance notes (high level)

- Ping improved vs WCF (reported ~1147ms UDP vs ~1500ms WCF), but still far worse than baseline ping to server (~190ms).
- Conclusion: transport switch alone not sufficient; the UDP implementation must avoid “TCP-like” synchronous patterns that inflate latency.

## 8) Open items / next tasks (carry into next chat)

1) Fix UDP client socket lifecycle:
   - Ensure Bind or Connect is performed before Receive.
   - Ensure receiveLoop uses correct remote endpoint handling.

2) Validate request/response matching:
   - Ensure pendingRequests removal uses requestId and does not discard valid responses.
   - Ensure msgType validation is done only for the matched requestId.

3) Revisit `receivePackets` long-poll semantics:
   - Server currently does `Thread.Sleep(maxWaitMs)` then tries once more.
   - Client currently loops immediately on `Ok None` and can spam the server.
   - Add minimal backoff / await strategy.

4) Throughput benchmarking:
   - Use iperf3 comparisons (novpn/vpn and -R variants) to quantify.
   - Decide whether the bottleneck is transport (WCF vs UDP) vs packet processing (NAT/external gateway) vs client loops.

5) ExternalGateway sendOutbound permission errors:
   - Confirm required privileges and OS constraints for raw socket send.
   - Determine if certain traffic (broadcast, specific ports) triggers forbidden send patterns.
