# vpn_gateway_spec__016__drop_limited_broadcast_outbound.md

## Purpose

Stabilize VPN gateway outbound behavior by **explicitly dropping limited IPv4 broadcast traffic** (`dst == 255.255.255.255`) before attempting to send it to the external network.

This change is **policy-level**, minimal, and surgical.  
No refactors, no performance tuning, no additional protocol handling.

---

## Background / Observed Problem

The VPN gateway server emits repeated errors of the form:

```
ExternalGateway.sendOutbound:
IPv4 UDP: <server-ip>:<ephemeral-port> -> 255.255.255.255:<port>
exception: An attempt was made to access a socket in a way forbidden by its access permissions.
```

Key facts:

- `dst = 255.255.255.255` (limited broadcast)
- protocol = UDP
- source IP = server public IP
- errors appear in **bursts**
- browsing still works, but gateway is slow and noisy

---

## Root Cause (Confirmed)

- Limited IPv4 broadcast (`255.255.255.255`) is **LAN-scoped by definition**
- Forwarding it to the public Internet is semantically meaningless
- Windows frequently blocks such sends on WAN interfaces
- Result: `WSAEACCES (10013)` socket exceptions in bursts

This traffic is **not required** for normal Internet access (HTTP(S), DNS, etc.).

---

## Required Change (MANDATORY)

### Drop outbound packets with:

```
IPv4.destination == 255.255.255.255
```

**before** any attempt to send them via sockets.

---

## Scope of Change

- Location: **server-side outbound path only**
- Affects: **sendOutbound / ExternalGateway outbound logic**
- Protocols affected: **all**, but observed only with UDP
- No changes to:
  - client code
  - send/receive loop design
  - NAT logic
  - ICMP handling
  - performance tuning
  - logging levels (except optional info log)

---

## Exact Behavioral Requirements

1. **Detection**
   - If IPv4 destination address equals `255.255.255.255`

2. **Action**
   - Silently drop the packet
   - Do **not** attempt to send it via socket
   - Do **not** throw

3. **Logging**
   - Optional, but recommended at `Info` or `Trace`:
     ```
     Dropping outbound limited broadcast packet (255.255.255.255)
     ```
   - Must NOT be logged as error

4. **No side effects**
   - No retries
   - No socket creation
   - No exception propagation

---

## Explicit Non-Goals (DO NOT DO)

Claude Code **must not**:

- Add multicast handling
- Add firewall/WFP rules
- Enable SO_BROADCAST
- Rewrite broadcast addresses
- Introduce configuration switches
- Add feature flags
- Touch timing / sleep / async loops
- Refactor networking architecture
- Add TODOs or alternative approaches

This spec is intentionally narrow.

---

## Rationale

- Limited broadcast is LAN-only
- VPN goal is **internet tunneling**, not LAN discovery forwarding
- Dropping this traffic:
  - removes socket permission errors
  - reduces load and noise
  - improves stability
  - does not break browsing

---

## Acceptance Criteria

After implementation:

- No outbound send attempts to `255.255.255.255`
- No `WSAEACCES / access permissions` errors related to broadcast
- Browser functionality unchanged
- Logs free of broadcast-related error bursts

---

## Workspace Reminder

Claude Code must work **only** inside:

```
C:\GitHub\Softellect\Vpn\
```

No files outside this tree may be touched.

---

## End of Spec
