# vpn_gateway_spec__038__udp_push_receive_packets.md
## Goal

Implement the **ONLY** protocol: **UDP_Push**.

Claude Code (CC) must implement the missing server method:

- `member this.receivePackets clientId` in:
  - `C:\GitHub\Softellect\Vpn\Server\Service.fs`

This method currently contains:

- `failwith $"FUBAR for clientId: '{clientId.value}'."`

That `failwith` must be removed and replaced with real functionality.

## Non‑negotiable constraints

1. **UDP_Push is the one and only dataplane protocol.**
2. **DO NOT add, restore, reference, or reintroduce any other protocols** (no WCF dataplane, no “legacy UDP”, no “combined protocol”, no fallback branches).
3. **Make the smallest possible change set**:
   - Primary change: `Server\Service.fs`.
   - Only touch other files if compilation forces it (and then keep changes surgical).

## Current architecture (push-only)

- Client ↔ Server dataplane is **UDP push**:
  - Client sends packets using `VpnPushUdpClient` (UDP datagrams with Push header).
  - Server receives UDP datagrams in `Server\UdpServer.fs` and calls `IVpnPushService.sendPackets`.
  - Server sends packets back to clients from `UdpServer.pushSendLoop` by draining each client session’s `pendingPackets` queue and sending push DATA datagrams.

- The server’s queue for outbound-to-client packets is:
  - `ClientRegistry.PushClientSession.pendingPackets : BoundedPacketQueue`
  - These are filled by:
    - `PacketRouter` routing (VPN subnet → client)
    - NAT inbound delivery (internet → client)
    - DNS proxy / ICMP proxy enqueue replies
    - Any server-side flow uses `registry.enqueuePacketForClient(...)` (or `enqueuePushPacket`)

✅ **Therefore: the authoritative source of “packets to deliver to client” is `pendingPackets` in the push session.**

## What `receivePackets` MUST do

Even though UDP_Push is push-based and the UDP server is the normal sender, `IVpnPushService.receivePackets` exists and must be implemented correctly.

Implement it as a **drain-from-queue** operation:

### Inputs/Outputs

- Input: `clientId : VpnClientId`
- Output: `VpnPacketsResult = Result<byte[][] option, VpnError>`

### Semantics

1. **Session required**
   - If there is no push session for `clientId`:
     - return `Error (clientId |> SessionExpiredErr |> ServerErr)`
     - and log at **Info** level that the session is missing.
2. **Update activity**
   - If session exists, call:
     - `registry.updateActivity(clientId)`
3. **Drain packets**
   - Drain up to a fixed maximum number of packets from:
     - `session.pendingPackets`
   - Use the existing `dequeueMany(maxCount)` on `BoundedPacketQueue`.
4. **Return value**
   - If drained count is **0**:
     - return `Ok None`
   - If drained count is **> 0**:
     - return `Ok (Some packets)`
5. **Logging**
   - Keep logs lightweight:
     - `Trace`: drained count and total bytes.
     - **Do not** dump packet contents unless the code already has such a mechanism and it is explicitly `Trace`.
6. **No endpoint logic here**
   - `receivePackets` is not responsible for UDP endpoint freshness; that is handled by UDP send loop logic.
   - Do not add “endpoint stale” logic into this method.

### Recommended constants (in `Service.fs`)

Add constants near the top of `module Service` (or near `VpnPushService`) to avoid magic numbers:

- `MaxReceivePacketsPerCall` (recommended: 256)
- `MaxReceiveBytesPerCall` (recommended: 256 * 1500 = 384000) **optional**
  - If implemented, stop draining once byte limit is exceeded.

**Important:** Keep it simple. Packet-count cap alone is enough.

## Error handling rules

- Do not throw exceptions.
- Do not `failwith`.
- Return `Result` values only.
- Use the existing error constructors already used by `sendPackets`:
  - missing session → `SessionExpiredErr |> ServerErr`

## Files to modify

1. **Required**
   - `C:\GitHub\Softellect\Vpn\Server\Service.fs`
     - Implement `IVpnPushService.receivePackets`

2. **Allowed only if required by compilation**
   - None expected. `BoundedPacketQueue` already provides `dequeueMany`.

## Acceptance checklist

- [ ] `failwith` removed from `receivePackets`
- [ ] `receivePackets` returns:
  - `Error (SessionExpiredErr ...)` when no session
  - `Ok None` when queue empty
  - `Ok (Some packets)` when packets available
- [ ] Uses `registry.updateActivity(clientId)` on success path
- [ ] Drains `pendingPackets` via `dequeueMany`
- [ ] No protocol additions, no new dataplane branches, no “legacy” resurrection
- [ ] Builds successfully

## Notes for CC (read carefully)

- This is **NOT** a place to “improve” architecture.
- **DO NOT** touch `UdpServer.fs` send/receive loops.
- Implement only the missing server method and keep changes minimal and obvious.
