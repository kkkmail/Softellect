# vpn_gateway_spec__020__replace_thread_sleep_with_channels.md

## Purpose

Remove all `Thread.Sleep` usage from the VPN codebase and replace polling-based loops with a **single, fixed, event-driven design** based on **System.Threading.Channels**.

This specification is **fully locked**.
There are **no optional paths**, **no alternatives**, and **no design choices left open**.

It is based on:
- `vpn_gateway_spec__017__thread_sleep_inventory.md`
- `vpn_gateway_spec__018__thread_sleep_inventory_results.md`

---

## Absolute rules (must be followed exactly)

1. **No `Thread.Sleep` anywhere** in F# code after this change.
2. **No polling loops**.
3. **No retries, no backoff, no delays**.
4. **No protocol awareness** (packet is always `byte[]`).
5. **No batching limits** (drain everything available).
6. **No channel completion semantics** during normal operation.
7. **No C# code** and no new cross-language abstractions.
8. **No new packet types** or wrapper records.

---

## Packet definition (locked)

- A packet is exactly `byte[]`.
- Channels transport **only `byte[]`**.
- Channel code must not inspect packet contents.

---

## Channel design (locked)

### Channel type

- Use **unbounded** channels only.
- Channel type: `Channel<byte[]>`.

### Channel options (must be explicit)

Create channels using **exactly** these options:

- `SingleReader = true`
- `SingleWriter = false`
- `AllowSynchronousContinuations = false`

No other options are allowed.

---

## Channel consumption semantics (LOCKED)

### Waiting rule

- The **only** operation that may wait is `ReadAsync(cancellationToken)`.

### Non-waiting rule

- `TryRead`:
  - never waits
  - returns immediately
  - used only to drain already-available items

### Forbidden API

- `ReadAllAsync` is **forbidden**.
- Channel completion is **not used** for control flow.

---

## Canonical consumer loop (conceptual)

1. Wait until at least one packet exists using `ReadAsync`
2. Drain all additional available packets using `TryRead` in a loop
3. Process all drained packets
4. Repeat

---

## Producer semantics (locked)

- All producers write `byte[]` into a `Channel<byte[]>`.
- No dropping.
- No throttling.
- No sleeps.

---

## Tunnel semantics (LOCKED)

The tunnel layer **must behave as a blocking producer**.

- Block until at least one packet exists
- Emit packets immediately as `byte[]`
- No polling
- No sleeps
- No per-packet `Task.Run`

---

## Files that must be modified

1. `Client\Service.fs`
   - `VpnClientService.sendLoop`
   - `VpnClientService.receiveLoop`

2. `Client\Tunnel.fs`
   - `Tunnel.receiveLoop`

3. `Server\ExternalInterface.fs`
   - `ExternalGateway.receiveLoop`

4. `Server\PacketRouter.fs`
   - `PacketRouter.receiveLoop`

---

## Error handling (locked)

- Catch exception
- Log
- Exit or continue per existing structure
- No retries
- No delays

---

## Acceptance criteria

- No `Thread.Sleep` remains in F# files.
- Packet flow unchanged.
- CPU idle usage near zero.
- No polling loops remain.

---

## Deliverables

- Updated F# files only.
- No new abstractions.
- No optional logic.

This spec is final. Implement exactly this.
