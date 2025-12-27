# NAT Exhaustion Investigation Report

## Executive Summary

The error `"NAT: no free external ports/ids"` occurs because **`removeStaleEntries` is never called**. The NAT module defines a cleanup function but no code in the system invokes it. NAT mappings are created indefinitely and never removed, leading to guaranteed exhaustion over time.

---

## 1. Exhaustion Detection

### Where the error is generated

**File:** `C:\GitHub\Softellect\Vpn\Server\Nat.fs`
**Function:** `allocateExternalPortOrId` (lines 62-79)
**Line:** 77

```fsharp
if not found then
    failwith "NAT: no free external ports/ids"
```

### Condition for error

The error occurs when:
- The allocator loops through all 65,535 possible port values
- Every candidate port already exists as a key in `tableByExternal`
- The loop counter `tries` reaches 65,535 without finding a free port

---

## 2. Definition of "external ports/ids"

### What they are

"External ports/ids" are 16-bit unsigned integers (`uint16`) stored in the `externalPortOrId` field. They serve as:
- **For TCP/UDP:** The external source port used when rewriting outbound packets
- **For ICMP:** The external ICMP identifier used when rewriting ICMP Echo Requests

### Single pool, protocol-separated

The NAT uses a **single allocation mechanism** (`allocateExternalPortOrId`) but keys are **protocol-specific**. The `NatExternalKey` struct combines:
- `externalPortOrId: uint16`
- `protocol: Protocol` (TCP, UDP, ICMP, or Other)

This means port 40000 for TCP and port 40000 for UDP are **distinct** entries in `tableByExternal`.

---

## 3. Pool Size and Limits

### Total pool size

**Theoretical maximum:** 65,535 entries per protocol (ports 1-65535, excluding 0)

**Effective pool per protocol:**
- Ports 0 and 1-1023 are skipped (reserved ports)
- Starting port is 40000
- Effective range: 40000-65535 initially, wrapping to 1024-65535

**Total effective entries per protocol:** ~64,512 ports

### Allocation logic

From `Nat.fs:62-79`:

```fsharp
let mutable candidate = !nextPort
...
if candidate = 0us || candidate < 1024us then
    candidate <- 40000us
```

The allocator:
1. Starts from the global `nextPort` (initialized to 40000)
2. Increments linearly with wraparound at 65535→0
3. Skips ports 0 and 1-1023
4. Bumps `nextPort` after allocation

### No off-by-one or wraparound risks in allocation

The allocation logic correctly handles wraparound. The risk is not in allocation correctness but in **permanent accumulation** due to missing cleanup.

---

## 4. Allocation Path

### Where allocation occurs

**Function:** `getOrCreateMapping` (lines 285-308)
**Called by:** `translateOutbound` (lines 345, 367)

### Data structures

| Dictionary | Key Type | Value Type | Purpose |
|------------|----------|------------|---------|
| `tableByExternal` | `NatExternalKey` | `NatEntry` | Lookup by external port for inbound translation |
| `tableByInternal` | `NatKey` | `NatExternalKey` | Lookup by internal flow for outbound translation |

### Allocation type

**Per-flow, lazy allocation:**
- Each unique 5-tuple (srcIp, srcPort, dstIp, dstPort, protocol) gets one NAT mapping
- Allocation happens on first outbound packet for that flow
- The same flow reuses its existing mapping (with `lastSeen` update)

### Atomicity

**Not fully atomic:** The code uses `ConcurrentDictionary` but does **not** use atomic operations like `TryAdd` or `AddOrUpdate` with factory functions. However, this is not the cause of exhaustion—it may only cause duplicate allocations in rare race conditions.

---

## 5. NAT Key Stability

### Key composition

The `NatKey` struct (lines 24-32) includes:
- `internalIp: uint32` (client VPN IP)
- `internalPortOrId: uint16` (client source port or ICMP ID)
- `remoteIp: uint32` (destination IP)
- `remotePort: uint16` (destination port, 0 for ICMP)
- `protocol: Protocol`

### Stability assessment

The key uses **stable flow properties**. There is **no per-packet uniqueness risk**. The same TCP/UDP connection or ICMP sequence will reuse its mapping.

### Key growth pattern

Under normal use with two clients:
- Each new outbound connection creates one mapping
- Common flows: HTTP/HTTPS connections (short-lived), DNS (handled separately by DnsProxy), ICMP ping
- With web browsing: dozens to hundreds of new connections per hour
- Over 24 hours with low traffic: potentially thousands of mappings

---

## 6. Release / Expiration — **ROOT CAUSE**

### Cleanup function exists but is never called

**File:** `C:\GitHub\Softellect\Vpn\Server\Nat.fs`
**Function:** `removeStaleEntries` (lines 84-88)

```fsharp
let removeStaleEntries (maxIdle: TimeSpan) =
    for kv in tableByExternal do
        if now() - kv.Value.lastSeen > maxIdle then
            tableByExternal.TryRemove kv.Key |> ignore
            tableByInternal.TryRemove kv.Value.key |> ignore
```

### Evidence that `removeStaleEntries` is never invoked

A comprehensive search of both codebases reveals:
- **`C:\GitHub\Softellect\Vpn\`**: The only occurrence is the function definition itself
- **`C:\GitHub\Softellect\Apps\Vpn\`**: No occurrences

There is:
- No timer that calls `removeStaleEntries`
- No loop that periodically invokes it
- No call site in any server startup, hosted service, or packet processing path

### `lastSeen` updates correctly

The `lastSeen` field is updated on:
- Outbound translation (line 291, 345): when reusing existing mapping
- Inbound translation (line 423, 453): when receiving return traffic

However, since cleanup never runs, `lastSeen` values are never evaluated for expiration.

---

## 7. Failure and Exception Paths

### Allocation-then-failure paths

In `getOrCreateMapping` (lines 285-308):

**Path 1 (lines 287-300):** Existing internal key found
- If `tableByExternal` lookup fails (inconsistent state), a new port is allocated
- The new mapping is written to both tables
- **Risk:** If an exception occurs after line 298 (`tableByExternal[newExtKey] <- entry`) but before line 299 (`tableByInternal[key] <- newExtKey`), the tables become inconsistent. This could cause orphaned entries.

**Path 2 (lines 301-308):** New mapping
- Port is allocated
- Both tables are updated
- Similar exception risk as Path 1

### Exception handling in packet processing

In `translateOutbound` and `translateInbound`:
- Exceptions from `getOrCreateMapping` (including `failwith` for exhaustion) propagate up
- `PacketRouter.processPacket` catches errors (line 397-399 in `PacketRouter.fs`) and logs them
- Packets are dropped on error

**No release on exception:** If allocation succeeds but later processing fails, the NAT entry remains.

---

## 8. Concurrency Safety

### Shared mutable structures

| Structure | Type | Thread Safety |
|-----------|------|---------------|
| `tableByExternal` | `ConcurrentDictionary<NatExternalKey, NatEntry>` | Thread-safe |
| `tableByInternal` | `ConcurrentDictionary<NatKey, NatExternalKey>` | Thread-safe |
| `nextPort` | `ref 40000us` (F# reference cell) | **Not thread-safe** |
| `NatEntry.lastSeen` | `mutable DateTime` | **Not thread-safe** (torn reads possible) |

### Race conditions

**`nextPort` race:**
- Multiple threads can read the same `nextPort` value
- Both may try to allocate the same candidate port
- One will succeed in `ContainsKey` check, the other will increment and retry
- **Impact:** Minor inefficiency, not exhaustion

**`lastSeen` race:**
- Multiple threads may update `lastSeen` concurrently
- DateTime is 8 bytes; on 32-bit runtime, writes are not atomic
- **Impact:** Potential torn read, not exhaustion

**Table consistency race:**
- `getOrCreateMapping` updates two tables non-atomically
- **Impact:** Possible orphaned entries in edge cases

---

## Confirmed Root Cause

**The NAT pool exhausts because `removeStaleEntries` is never called.**

The cleanup function exists and is correctly implemented, but:
1. No timer invokes it periodically
2. No packet processing loop calls it
3. No hosted service lifecycle method triggers it

Mappings accumulate indefinitely. With two clients over ~24 hours of low but continuous traffic (web browsing, background services), the number of unique flows can exceed the available port pool (~64,512 per protocol).

---

## Supporting Evidence

1. **Restart resolves the issue:** When the server restarts, static module state (`tableByExternal`, `tableByInternal`) is reinitialized to empty dictionaries.

2. **Clients not restarted:** Client reconnection does not affect server-side NAT state. The NAT tables persist across client reconnections.

3. **Low traffic, long uptime:** Even low traffic accumulates mappings. A single web browsing session can create hundreds of connections. Over 24 hours, thousands of stale entries accumulate.

---

## Additional Observations

### ICMP Proxy has similar structure

`IcmpProxy.fs` maintains its own `pendingRequests` dictionary with no cleanup. However:
- ICMP entries are keyed by (identifier, sequence) which cycles quickly
- ICMP traffic volume is typically much lower than TCP/UDP
- The dictionary uses `TryRemove` on successful reply (line 202)

The ICMP proxy is less likely to exhaust but could also accumulate entries for unanswered pings.

### DNS Proxy does not use NAT

`DnsProxy.fs` handles DNS separately, forwarding queries directly to upstream and building reply packets. It does not consume NAT ports.

---

## Conclusion

The NAT exhaustion is caused by a **missing invocation** of the existing cleanup function. The `removeStaleEntries` function in `Nat.fs` is defined but never called, resulting in unbounded growth of NAT mappings until the port pool is fully consumed.
