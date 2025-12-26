# vpn_gateway_spec__000__general

## Working directories
Claude Code must work only under the following directories:

```
C:\GitHub\Softellect\Vpn\
C:\GitHub\Softellect\Apps\Vpn\
```

No files outside these directories may be examined or modified.

---

## Goal

Implement and maintain a user-space VPN gateway where **all traffic from VPN clients (10.66.77.0/24)** is forwarded through the VPN server and exits to the real internet using a **user-space external interface + user-space NAT**.

No OS routing, RRAS, Windows ICS, or system NAT tables may be used or modified.

The user (not Claude Code) will:
- build the solution,
- run the VPN,
- execute tests,
- validate behavior.

Claude Code must **never build or run** the solution.

---

## Current architecture (high level)

### Client side
- A WinTun adapter is created on the client.
- Client is assigned an IP in the VPN subnet (example: `10.66.77.2/24`).
- The tunnel captures IPv4 packets from the WinTun adapter (`Tunnel.receiveLoop`).
- Captured packets are batched and sent to the server over a WCF `NetTcpBinding` service (`sendPacket`).
- Client receives batches of packets from the server (`receivePackets`) and injects them into WinTun (`Tunnel.injectPacket`).

### Server side
- A WinTun adapter is created on the server.
- The server WinTun adapter is assigned the VPN gateway IP (example: `10.66.77.1/24`).
- `PacketRouter.receiveLoop` receives packets from the server WinTun adapter and routes them:
  - If destination IP is inside the VPN subnet → enqueue packet to the correct VPN client session (by assigned IP).
  - If destination IP is outside the VPN subnet → forward to the internet:
    - Apply user-space NAT (`Nat.translateOutbound`) so:
      - source IP becomes the server public IP,
      - source port / id becomes an allocated NAT value.
    - Send the full IPv4 packet to the internet using `ExternalGateway.sendOutbound` (raw IPv4 socket).

### Return traffic (internet → client)
- Server external interface (`ExternalGateway`) receives raw IPv4 packets from the internet.
- Received packets are passed to the NAT inbound path:
  - `Nat.translateInbound` finds the NAT mapping by **destination port/id on the server public IP**.
  - It rewrites destination IP/port back to the internal VPN client IP/port.
- The translated packet is injected into the server WinTun adapter.
- `PacketRouter.receiveLoop` receives that packet and routes it to the correct VPN client by destination IP.

### DNS
- A DNS proxy exists on the server (`Softellect.Vpn.Server.DnsProxy`).
- DNS queries are IPv4 UDP packets forwarded to upstream resolvers.
- Correct NAT inbound/outbound handling for UDP is required for DNS reliability.

---

## Lifecycle and background task rules (CRITICAL)

These rules are mandatory for all server-side changes.

### Background loops / tasks
Any background loop or periodic task (cleanup, monitoring, maintenance, etc.) must:

1. Be started **exactly once per server process**.
2. Be started during normal server startup.
3. Respect the application / service cancellation token.
4. Terminate cleanly on shutdown.
5. Be non-blocking for packet processing paths.
6. **Must NOT** use `Thread.Sleep`.
7. Use async delays or equivalent cancellation-aware waiting.

### Long-lived state
Any long-lived, accumulating state (NAT tables, session maps, caches, etc.) must have:

- explicit lifecycle management,
- cleanup or expiration logic,
- a clearly identifiable place where cleanup is invoked.

Relying on process restart for cleanup is **not acceptable**.

---

## Spec modes and authority

Each spec file (`vpn_gateway_spec__NNN__*.md`) defines its own **mode**.

Claude Code must strictly follow the mode implied by the spec:

### Analysis-only specs
- CC must inspect code and report findings only.
- CC must NOT modify code.
- CC must NOT propose fixes unless explicitly requested.

### Implementation specs
- CC must implement exactly what is specified.
- CC must NOT refactor unrelated code.
- CC must NOT introduce optional designs or alternatives.

If a spec is unclear, CC must ask questions **before** making changes.

---

## Constraints / invariants

- Do not modify OS routing tables, RRAS, netsh NAT, Windows ICS, etc.
- Keep VPN subnet IPv4-only for now; IPv6 packets may be dropped.
- NAT and external gateway operate on **raw IPv4 packets** (IP header included).
- Any change must be small, localized, and directly related to the active spec.
- Avoid refactors unless explicitly requested.
