# vpn_gateway_spec__029__cool_down_packetrouter_receive_thread.md

## Goal

Cool down the server by preventing `PacketRouter.receiveLoop` from burning a full CPU core continuously when the WinTun adapter has a constant stream of packets (even with no VPN client connected).

**This change MUST ONLY address CPU burn in the receive thread.**
Do NOT change NAT/DNS/ICMP logic, routing logic, log levels, or packet parsing behavior.

If anything is unclear, ask questions **before** editing code.

---

## Target file

- `C:\GitHub\Softellect\Vpn\Server\PacketRouter.fs`

---

## Problem Summary (what the log proves)

The log shows:

- `wk=1` (only one wakeup)
- `empty=0` (ReceivePacket never returned empty)
- `rx=57,715,816` (tens of millions of packets drained/processed)
- `proc≈95%` (thread spends almost all time processing/draining)

This means the code enters `drainPackets` once and then stays inside the inner drain loop “forever” because “drain until empty” never becomes empty under constant traffic.

---

## Required Fix

### A) Hard cap the work per wakeup (MANDATORY)

**Implement a strict limit on how many packets are drained and processed per single wakeup.**

Use:

- `maxPacketsPerWakeup = 4096`

Rationale: A constant packet stream means `ReceivePacket()` may never go empty. We must return to the outer loop periodically.

### B) Cooperative yield after hitting the cap (MANDATORY)

When `maxPacketsPerWakeup` is reached inside `drainPackets`, you MUST:

1. Stop draining immediately (exit `drainPackets`), and
2. Call `Thread.Yield()` exactly once (as the last step before returning).

This ensures the OS scheduler can run other work and the receive thread does not monopolize a core continuously.

### C) Always reset the read event when you stop draining (MANDATORY)

**Call `resetReadEventIfPossible readEvent` whenever draining stops**, including:

1. `ReceivePacket()` returned null/empty.
2. `maxPacketsPerWakeup` was reached.
3. Cancellation was requested.

This is required to avoid a stuck “signaled” state.

### D) Respect cancellation inside the drain loop (MANDATORY)

Inside the loop that drains remaining packets, every 256 packets, check:

- `cts.Token.IsCancellationRequested`

If cancellation is requested, stop draining immediately and return.

---

## Exact Implementation Instructions (no deviations)

### 1) Add constants

Add these literals:

```fs
let [<Literal>] maxPacketsPerWakeup = 4096
let [<Literal>] cancelCheckEveryPackets = 256
```

Do NOT make them configurable. Do NOT add options.

### 2) Modify `drainPackets` only

Update the existing helper:

```fs
let drainPackets (st: ReceiveLoopState) (adp: WinTunAdapter) (readEvent: WaitHandle) =
```

Implement this exact algorithm:

- Maintain `processedThisWakeup : int` (mutable), initialized to 0.
- Read first packet using existing timed drain call.
- If first packet is empty:
  - increment `st.emptyWakeups`
  - call `resetReadEventIfPossible readEvent`
  - return

- Else:
  - process it (existing timed process)
  - increment `st.packetsRx`
  - increment `processedThisWakeup`

- Then loop:
  - If `processedThisWakeup >= maxPacketsPerWakeup`:
    - call `resetReadEventIfPossible readEvent`
    - call `Thread.Yield()` (exactly once)
    - return
  - Every `cancelCheckEveryPackets` packets:
    - if `cts.Token.IsCancellationRequested` then:
      - call `resetReadEventIfPossible readEvent`
      - return
  - Receive next packet (timed drain)
  - If empty:
    - call `resetReadEventIfPossible readEvent`
    - return
  - Else:
    - process it (timed process)
    - increment counters and `processedThisWakeup`
    - continue

Constraints:
- Do NOT add any extra logging.
- Do NOT change how `processPacket` works.
- Do NOT change wait logic or interval logging logic.
- Do NOT change timers/counters other than incrementing as above.

### 3) Do NOT change outer loop logic

Do not change:
- `waitForEvent`
- `handleWaitResult`
- `logStatsIfNeeded`
- Any routing/NAT/DNS/ICMP logic

Only the `drainPackets` helper is being changed for cooling.

---

## Acceptance Criteria

After this change:

1. With **server running and no VPN client connected**, the PacketRouter receive thread must not hold 100% CPU constantly.
2. The stats line should show `wk` increasing over time (not stuck at 1 forever).
3. Server shutdown should not remain “hot” for long.

---

## Deliverable

- Commit the change in `PacketRouter.fs` only.
- Provide a short summary of what changed and where.
- If you encounter anything unclear, stop and ask questions before proceeding.
