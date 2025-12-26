# vpn_gateway_spec__013__fix_tcp_icmp_routing.md

## Goal

Fix VPN routing for **TCP and ICMP** (tests by **explicit IP**, no DNS reliance):
- `ping <ip>`
- `tracert -d <ip>`
- `curl http://<ip>/` (or `curl https://<ip>/` if applicable)

**Do not change DNS behavior** (leave the partially working DNS as-is).

## Non-negotiable conventions

- **No duplicate code**. If the same logic appears more than once, extract a helper.
- **F# uses camelCase** for functions/values.
- If you see **inconsistent instructions**, **ask for confirmation** before coding.
- **Do not build** the solution. Only implement code changes.
- Touch only the files listed below.

## Files to change (only these)

1) `C:\GitHub\Softellect\Vpn\Server\Nat.fs`  
2) `C:\GitHub\Softellect\Vpn\Server\PacketRouter.fs`

Do **not** change any other files.

## Summary of required fixes

### A) Fix server inbound delivery path (PacketRouter)

**Problem:** inbound packets received from the external interface must be delivered to the VPN client via `registry.enqueuePacketForClient`.  
They must **NOT** be injected into the server OS stack with `WinTunAdapter.SendPacket(...)`.

**Required change:**
In `PacketRouter.fs`, in the `externalGateway.start(fun rawPacket -> ...)` callback:

- Keep calling `translateInbound externalIpUint rawPacket`.
- If it returns `Some translated`:
  - Determine `translated` destination IP (IPv4 dst bytes 16..19).
  - Find the VPN client session by assigned IP (already have `findClientByIp : IpAddress -> ...`).
  - If found: `registry.enqueuePacketForClient(session.clientId, translated) |> ignore`.
  - If not found: log trace that no client session exists for that destination.
- Do **not** call `adp.SendPacket(translated)` in this inbound callback.

Rationale: `SendPacket()` injects into the **server OS** networking stack; it does not feed the packet back into the `ReceivePacket()` loop for routing.

### B) Fix NAT mapping collisions (Nat)

**Problem:** current NAT key is only `{ internalIp; internalPort; protocol }`.  
This collides for TCP (and UDP) because multiple flows can reuse the same internal port to different remote endpoints.

**Required change:**
Update NAT state and keying to include the remote endpoint:

- Expand the internal-flow key to include:
  - `remoteIp : uint32` (network byte order)
  - `remotePort : uint16` (host order)
- Also, the external reverse-lookup must avoid TCP/UDP port collisions:
  - Key `tableByExternalPort` by `(externalPort, protocol)` (or use a dedicated `NatExternalKey`).

**Net result:**
Each flow is identified by (internal ip/port, remote ip/port, protocol), and reverse mapping uses (external port, protocol).

### C) Add ICMP NAT (Nat)

**Problem:** ICMP (ping / tracert) cannot work without NAT.  
Right now ICMP is treated as `Other` and passes through unchanged, which breaks return traffic.

**Required change:**
Implement minimal IPv4 ICMP NAT support:

- Support at least:
  - ICMP Echo Request (type 8) outbound NAT
  - ICMP Echo Reply (type 0) inbound reverse NAT
- Use ICMP **Identifier** field (16-bit) as the NAT “port-like” value.
- For outbound echo request from VPN subnet:
  - allocate an external identifier (similar to externalPort allocation)
  - rewrite:
    - IPv4 source address -> `externalIp`
    - ICMP identifier -> allocated external identifier
  - update:
    - IPv4 header checksum
    - ICMP checksum
  - store mapping so that inbound echo replies can be translated back
- For inbound echo reply addressed to `externalIp`:
  - lookup mapping using `(icmpIdentifier, protocol=icmp)` (and optionally remoteIp)
  - rewrite:
    - IPv4 destination address -> internal client IP
    - ICMP identifier -> original internal identifier
  - update checksums

Note: `tracert` may also rely on ICMP Time Exceeded. Do not implement Time Exceeded yet unless you can do it cleanly without destabilizing echo support; focus on echo first so `ping` works.

## Implementation details you must follow

### 1) Protocol representation in Nat

Keep the existing `Protocol` DU but extend it so ICMP is explicit:

- Add `Icmp` as a case.

Update `getProtocol` to recognize ICMP:
- IPv4 protocol byte 1 => ICMP.

### 2) NAT tables

Update the dictionaries to reflect new keys:

- `tableByInternal : ConcurrentDictionary<NatKey, NatExternalKey>`
- `tableByExternal : ConcurrentDictionary<NatExternalKey, NatEntry>`

Where:
- `NatKey` includes:
  - internalIp (uint32, network order)
  - internalPortOrId (uint16, host order)   // TCP/UDP port or ICMP identifier
  - remoteIp (uint32, network order)
  - remotePort (uint16, host order)         // 0 for ICMP echo
  - protocol
- `NatExternalKey` includes:
  - externalPortOrId (uint16, host order)   // TCP/UDP external port or ICMP external identifier
  - protocol

Use one allocator for `externalPortOrId` (uint16) with the same “skip reserved <1024” logic.

### 3) ICMP parsing + checksum

Implement small helpers (no duplication) in `Nat.fs`:

- Detect ICMP echo request / reply:
  - ICMP header starts at `ihl`
  - `icmpType = packet[ihl]`
  - `identifier` at `ihl + 4`..`ihl + 5`
- Compute ICMP checksum over the entire ICMP message (`totalLen - ihl`), with the ICMP checksum field zeroed during calculation, then written back.

### 4) PacketRouter integration

Do not alter the DNS proxy branch at all.

Only change:
- the NAT outbound call remains:
  - `translateOutbound (...) externalIpUint packet` then `externalGateway.sendOutbound(natPacket)`
- the NAT inbound callback changes to enqueue packets to clients (section A).

## Minimal logging requirements (do not spam)

- Keep NAT logs but adjust them to print:
  - protocol
  - internal ip/port/id
  - remote ip/port
  - external port/id
- In PacketRouter inbound callback:
  - log trace only on missing client mapping.

Do **not** refactor `summarizePacket` in this change.

## Acceptance criteria

After applying the patch:

1) From the VPN client machine:
   - `ping <somePublicIp>` must show replies.
2) `tracert -d <somePublicIp>` should show hops (may still be imperfect if Time Exceeded mapping is missing).
3) `curl http://<publicIp>/` should send traffic through VPN (verify by server logs showing NAT for TCP).

If (2) fails but (1) succeeds, that is acceptable for this step.

## Deliverable

Produce code changes only in the two specified files and ensure the solution compiles.
