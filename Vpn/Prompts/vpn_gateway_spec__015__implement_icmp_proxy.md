# vpn_gateway_spec__015__implement_icmp_proxy.md

## Purpose

Implement **ICMP proxying (Echo Request / Echo Reply only)** in the VPN server,
analogous in spirit to the existing `DnsProxy`, with **minimal and surgical changes**.

This spec intentionally avoids touching DNS, TCP, routing, logging, or refactoring.
Claude Code (CC) must **only** implement ICMP proxy functionality and wire it in.

CC must **NOT** build, run, or test the solution.

---

## Scope (Strict)

### IN SCOPE
- ICMP Echo Request (Type 8) and Echo Reply (Type 0)
- IPv4 only
- Proxy-style handling:
    - client → server → internet
    - internet → server → correct client
- Minimal ICMP state tracking (id/sequence based)
- Wiring into existing packet flow

### OUT OF SCOPE (DO NOT TOUCH)
- DNS (leave `DnsProxy` exactly as-is)
- TCP (do not modify NAT or TCP handling)
- IPv6
- Logging refactors
- Build / test / run
- Cleanup, eviction, optimizations (unless strictly required)

---

## High-Level Design

### Model

ICMP handling must follow the **same architectural idea as DNS proxy**:

- Detect ICMP Echo Requests sent **to outside addresses**
- Rewrite and forward them via the external interface
- Remember enough state to map replies back to the originating client
- On receiving ICMP Echo Replies from the internet:
    - Match them to a stored request
    - Rewrite destination back to the client
    - Enqueue packet for that client

This is **not NAT**, and **not passthrough**.

---

## New Module

Create a new module: C:\GitHub\Softellect\Vpn\Server\IcmpProxy.fs


### Responsibilities
- Parse IPv4 ICMP packets
- Identify Echo Request / Echo Reply
- Track outstanding Echo Requests
- Translate outbound and inbound packets

### Suggested State (minimal)

- Keyed by:
    - ICMP Identifier
    - ICMP Sequence
- Store:
    - clientId
    - client IP
    - timestamp (optional, no eviction required yet)

Exact data structure choice is up to CC, but keep it minimal.

---

## Packet Handling Rules

### Outbound (Client → Server)

When the server receives a packet from a client:

1. If packet is ICMP Echo Request (IPv4, proto=1, type=8)
2. And destination IP is **outside VPN subnet**
3. Then:
    - Record mapping (identifier + sequence → client)
    - Rewrite source IP to server public IP
    - Recompute checksums
    - Send via external gateway
4. Do **not** inject into TUN

### Inbound (Internet → Server)

When external gateway receives a packet:

1. If packet is ICMP Echo Reply (proto=1, type=0)
2. And matches a stored mapping
3. Then:
    - Rewrite destination IP back to client IP
    - Recompute checksums
    - Enqueue packet for the correct client
4. Otherwise, drop silently

---

## Wiring Points (Mandatory)

### 1. `processPacket` (Server)

In the server-side packet handling function (currently handling DNS vs inject):

- Add ICMP handling **before** the fallback path
- DNS remains first
- ICMP second
- Everything else unchanged

Pseudo-order:
```
DNS?
→ ICMP?
→ existing behavior
```

### 2. External Gateway Callback

Where inbound packets from the external interface are handled:

- Call `IcmpProxy.tryHandleInbound`
- If it returns a packet + client → enqueue
- Otherwise, fall through to existing NAT/TCP logic unchanged

---

## Checksums

- ICMP checksum **must** be recomputed after any modification
- IPv4 header checksum **must** be recomputed

Reuse existing helpers where possible.
Do not introduce new checksum libraries.

---

## Logging

- Minimal logging only
- Follow existing packet-level trace style
- No refactors of logging framework

---

## Safety Rules for CC

- **No duplicate code**
- **No renaming existing public functions**
- **No reformatting unrelated files**
- **No “nice-to-have” changes**
- If instructions conflict → STOP and ASK

---

## Acceptance Criteria (Human-Verified)

After CC changes (verified manually by human, not CC):

- `ping 8.8.8.8` over VPN produces Echo Reply
- `ping 9.9.9.9` over VPN produces Echo Reply
- DNS behavior unchanged
- TCP behavior unchanged
- No new logs unrelated to ICMP

---

## Explicit Reminder to CC

You are implementing **ICMP proxy only**.
Do not touch DNS.
Do not touch TCP.
Do not build.
Do not test.
Do not refactor.

If unsure: ask.
