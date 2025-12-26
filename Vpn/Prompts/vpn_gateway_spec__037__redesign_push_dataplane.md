# vpn_gateway_spec__037__redesign_push_dataplane.md

NNN = 037  
Status: **Draft for agreement** (no code changes yet)

## Why we’re restarting

Your current UDP tunnel design is fundamentally “request/response + polling”:

- Client has a **send loop** and a **receive loop** that repeatedly calls `receivePackets clientId` (even with wait/backoff).
- Server’s send-to-client path is effectively **client-driven long-poll** (semaphore wait, then dequeue).
- That creates: extra RTTs, head-of-line waiting, bursty delivery, and jitter amplification (matching your ping + speedtest behavior).

Most high-performance VPNs instead use a **push dataplane**:
- a continuous packet stream on a UDP socket
- server **pushes** packets to the client endpoint as soon as they exist
- the “control plane” is separate and infrequent

This spec proposes that redesign.

## What to learn from open-source VPNs (high-level)

### 1) WireGuard’s design principles
WireGuard’s public docs and papers emphasize minimizing complexity and allocations, avoiding overly stateful “connections,” using queueing/batching, and generally designing the protocol and implementation to be fast and simple in practice.  
Refs:
- WireGuard overview/docs: https://www.wireguard.com/  
- Kernel-integration / batching / queueing discussion (paper): https://www.wireguard.com/papers/wireguard-netdev22.pdf

### 2) OpenVPN DCO (data-channel offload)
OpenVPN’s “DCO” exists because user-space data plane is often the bottleneck; offloading the data path to a kernel module drastically reduces overhead and increases throughput.  
Ref: https://openvpn.net/as-docs/overview/openvpn-dco.html

### 3) Wintun / WireGuard-for-Windows I/O model
The Wintun API exposes a read-wait handle and ring-buffer-like packet APIs intended for high-throughput user-space tunneling with minimal overhead.  
Ref: https://github.com/WireGuard/wireguard-windows/blob/master/README.md

**Key takeaway for us:** even if we stay user-space, we must keep the dataplane “always-on” and avoid per-batch request/response patterns.

## Goals

1. **Stable latency** (no 200–2000ms ping swings caused by our tunnel mechanics).
2. **Much higher throughput** by eliminating polling/long-poll RPC patterns.
3. **Keep the codebase small and debuggable** (surgical changes once we agree).
4. **Preserve your current high-level structure**: WinTun capture/inject, server router/NAT/DNS/ICMP, per-client registry.
5. **No encryption/auth changes in this spec** (keep current auth model initially).

## Non-goals (for NNN=037)

- Replacing WinTun.
- Implementing WireGuard crypto/Noise handshake.
- Perfect reliability/ordering at tunnel layer (TCP already handles this; we just avoid self-inflicted loss).
- Multi-client mesh / roaming beyond “remember last UDP endpoint”.

## Proposed architecture (from scratch but aligned with your project)

Split into two planes:

### A) Control plane (rare)
- Authenticate / assign IP / config.
- Establish a **session key** in memory: `clientId -> assignedIp + publicKey + lastSeen + currentUdpEndpoint`.
- This can remain your existing `authenticate` flow (or even remain WCF for now).

### B) Data plane (hot path, always-on)
Single UDP socket per side, always running:
- Client: `UdpDataClient`
- Server: `UdpDataServer`

Both use **push** semantics:
- Client sends packets to server immediately after capture.
- Server sends packets to client immediately after enqueue.

There is **no “receivePackets” polling** on the data plane.

## Data-plane wire protocol (minimal)

All messages are single UDP datagrams. No fragmentation in v1; we instead control MTU.

### Datagram header (fixed)
```
magic      : uint32  (e.g., 0x56504E31 = "VPN1")
version    : uint8   (1)
msgType    : uint8   (DATA=1, KEEPALIVE=2, CONTROL=3)
flags      : uint16
clientId   : 16 bytes (Guid)
seq        : uint32  (monotonic per sender; wrap ok)
payloadLen : uint16
reserved   : uint16  (future)
payload    : payloadLen bytes
```
Rationale:
- fixed header: fast parse, avoids allocations
- `seq` enables basic diagnostics and optional drop detection

### MsgType=DATA
Payload is a raw IPv4 packet (as today).

### MsgType=KEEPALIVE
No payload. Sent periodically by client to keep NAT mapping alive and to update server’s view of client endpoint.

### MsgType=CONTROL (optional, later)
Could carry:
- server-to-client “reauth required”
- “MTU update”
- “client banned” etc.
But for NNN=037 we can skip CONTROL entirely.

## MTU strategy (critical)

Right now you’re fragmenting at the UDP protocol layer. That is almost always painful.

Instead:
- Set WinTun interface MTU so that the captured IP packets fit into a single UDP datagram without fragmentation on typical Ethernet paths.

Typical numbers (IPv4):
- Ethernet MTU: 1500
- IP(20) + UDP(8) = 28 bytes
- Our header ~ (4+1+1+2+16+4+2+2) = 34 bytes
- Total overhead ≈ 62 bytes
- Safe tunnel MTU ≈ 1500 - 62 = 1438

Pick a conservative MTU like **1400** (or **1380** if you want extra slack) for early tests.

**Spec requirement:** clamp outbound captured packets to MTU; if larger, drop with a counter (or later reintroduce fragmentation if needed).

## Queues and threading model (performance-oriented but simple)

### Client side
1. **TunReceive thread** (already exists in `Tunnel.receiveLoop`):
   - drains Wintun packets
   - writes to an outbound queue (bounded)
2. **UdpSend loop**:
   - reads from outbound queue
   - batches datagrams up to a byte cap or a short time budget (e.g., 0–1ms)
   - uses `Socket.SendTo` or `SendToAsync` on a single UDP socket
3. **UdpReceive loop**:
   - continuously receives datagrams
   - for DATA: inject into tun (or enqueue to a bounded “inject” queue)

**Bounded queues** are mandatory to prevent runaway latency:
- If outbound queue is full: drop newest or oldest (choose one and count it).

### Server side
1. **UdpReceive loop**:
   - continuously receives DATA from any client
   - updates `clientId -> endpoint` from sender endpoint
   - routes payload (existing router path)
2. **Per-client send queues** (bounded):
   - when `registry.enqueuePacketForClient` is called, it also signals server send loop
3. **UdpSend loop**:
   - multiplexes across clients that have pending packets
   - sends immediately (no waiting for client polls)

This matches how real systems avoid long-poll latency and jitter.

## Session endpoint binding

Server must remember **where** to send to:

- When server receives any valid datagram from `clientId`, it stores `(ip, port)` as `currentEndpoint` for that client.
- Server only sends DATA if an endpoint is known and “fresh” (e.g., lastSeen < 60s).
- Client sends KEEPALIVE every ~10s (configurable).

This is the minimal NAT traversal model used by many UDP tunnels.

## Minimal changes to your existing server pipeline

Keep these subsystems:
- `PacketRouter` (WinTun on server + NAT + DNS proxy + ICMP proxy)
- `ClientRegistry` as the source of truth for `clientId -> assignedIp` and for enqueue/dequeue

But change the “delivery to client” mechanism:
- remove (or deprecate) `receivePackets` polling in UDP tunnel mode
- replace with server-side push via UDP send loop

## Observability requirements (so we can measure real progress)

Add counters (in-memory, logged every 5s) for both sides:

### Client
- tun_rx_packets, tun_rx_bytes
- udp_tx_datagrams, udp_tx_bytes
- udp_rx_datagrams, udp_rx_bytes
- dropped_due_to_mtu
- dropped_due_to_queue_full_outbound
- dropped_due_to_queue_full_inject

### Server
- udp_rx_datagrams, udp_rx_bytes
- udp_tx_datagrams, udp_tx_bytes
- unknown_client_drops
- no_endpoint_drops
- per-client queue_full_drops

Also log:
- moving average and p95 of “tun->udp send latency” (enqueue timestamp to send time)
- moving average and p95 of “udp->tun inject latency”

## Phased implementation plan

### Phase 0: agree on design
- Approve header layout and MTU choice (initial 1400).
- Approve push model (no polling `receivePackets` for UDP tunnel).
- Approve bounded queues + drop policy.

### Phase 1: implement push dataplane for UDP tunnel
- Add `UdpDataServer` with receive loop + send loop.
- Add `UdpDataClient` with receive loop + send loop.
- Keep `authenticate` as-is for now (can remain WCF or reuse your UDP auth message).
- Keep current WinTun capture/inject code (Tunnel module) but change how packets go to/from UDP tunnel.

### Phase 2: refine for throughput
- Micro-batching on send (byte cap + time budget)
- Improve send-loop fairness across clients
- If needed: switch to `SocketAsyncEventArgs` / pooled buffers

### Phase 3: security + production
- Add per-datagram authentication (MAC) at least, then encryption if desired.
- Key rotation / replay defense (seq window).

## Open questions (we should decide now)

1. **Drop policy when outbound queue is full**: drop newest or oldest?
2. **MTU**: start at 1400, or more conservative 1380?
3. **Do we keep WCF for auth** initially, or also move auth to UDP?
4. **Per-client server send queue**: one queue per client (simple) vs a global queue with tagging (faster)?

---

## What I need from you (files / info) if we proceed to NNN=038
(Only if/when you say “yes, implement”.)

- `Server\UdpServer.fs` and `Client\UdpClient.fs` (current implementations).
- Any `WcfClient.fs` / WCF server code if auth depends on it.
- WinTun interop wrappers (the functions you use for Receive/Send/WaitHandle), if not already covered.
