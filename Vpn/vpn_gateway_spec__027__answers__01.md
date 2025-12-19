# vpn_gateway_spec__027__answers__01

This file answers CC’s questions and defines the **minimal** set of timing buckets + counters to add to `PacketRouter.receiveLoop`, to diagnose “spinning vs waiting” with the smallest surface area.

---

## Minimal timing buckets and counters

### Timers (Stopwatch instances)
Create **once** (outside the loop) and reuse:
- `swCalendar` — measures overall wall time since loop start (calendar stopwatch).
- `swWait` — measures time spent in `WaitHandle.WaitAny(...)`.
- `swDrain` — measures time spent draining packets (`ReceivePacket()` loop including first packet check).
- `swProcess` — measures time spent in `processPacket`.

Optional but high-value (only if kept clean):
- `swResetEvent` — measures time spent in `resetReadEventIfPossible`.

### Time accumulators (ticks, int64)
Accumulate **ticks** (not ms) to avoid precision loss:
- `calTicks` (interval calendar ticks, used as denominator)
- `waitTicks`
- `drainTicks`
- `processTicks`
- (optional) `resetEventTicks`

### Count accumulators (int64)
Each bucket gets a call-count (via `timed`):
- `waitCount`
- `drainCount`
- `processCount`
- (optional) `resetEventCount`

### Core counters (per 5s interval, reset after log)
Minimal set to explain CPU burn:
- `waitWakeups` — increment when `waitResult = 0` (readEvent fired).
- `waitTimeouts` — increment when `waitResult = WaitHandle.WaitTimeout`.
- `waitCancels` — increment when `waitResult = 1` (cts fired) **or** when `cts.IsCancellationRequested` is observed.
- `emptyWakeups` — increment when readEvent fired but first `ReceivePacket()` returns null/empty.
- `packetsRx` — increment for each non-empty packet processed.

**Why this is minimal:**  
If `wait%` is low but `waitTimeouts`/`emptyWakeups` are high, you’re spinning.  
If `wait%` is high, you’re actually blocked waiting (core burn is elsewhere).  
If `process%` is high, packet processing is heavy.  
If `drain%` is high, you’re spending time in `ReceivePacket()` loop (or copying).

---

## Formatting requirements

### Calendar time
Log elapsed wall time as `MM:SS.fff` from `swCalendar.Elapsed`.

### Percent formatting: `00.0000`
Compute each bucket percentage as integer “parts per 1,000,000” to avoid floats:

- `pct1e6 = bucketTicks * 1_000_000 / calTicks`
- To format `00.0000`:  
  - `whole = pct1e6 / 10_000`  
  - `frac  = pct1e6 % 10_000`  
  - print `"{whole:00}.{frac:0000}"`

This naturally yields `00.0000`..`100.0000`.

---

## Answers to CC questions

### Q1. `timed` helper signature: byref vs ref vs closures
Prefer **byref** for the hot path: no allocations, explicit, and fast.

Use an `inline` helper with `&mutVar` call sites:

```fs
let inline timed (sw: Stopwatch) (timeAcc: int64 byref) (countAcc: int64 byref) (action: unit -> 'T) : 'T =
    sw.Restart()
    let r = action()
    sw.Stop()
    timeAcc <- timeAcc + sw.ElapsedTicks
    countAcc <- countAcc + 1L
    r
```

Notes:
- Use `ElapsedTicks` (not ms).
- No `try/with` inside `timed` for now (per instruction).

### Q2. `waitTimeouts` counter
Yes — add `waitTimeouts` and increment it when:

- `waitResult = WaitHandle.WaitTimeout`

This is a key “spin vs wait” discriminator.

### Q3. Optional buckets
For this iteration: **skip optional buckets** unless they stay clean and don’t force restructuring.

If adding exactly one optional bucket, the most valuable is:
- `resetReadEventIfPossible` timing+count (`swResetEvent`, `resetEventTicks`, `resetEventCount`)

Reason: it tells you whether you are frequently trying to “unstick” a signaled event.

### Q4. Helper placement
Place helpers as **`let` bindings inside the type, immediately before `receiveLoop`** (private-to-type, closure-friendly, no member dispatch). Do **not** make them members unless needed elsewhere (not needed here).

---

## Suggested minimal log line (every 5 seconds)

Log at `info` (throttled by stopwatch interval):

Include:
- `t=MM:SS.fff`
- `%wait`, `%drain`, `%proc` (and optional `%reset`)
- counters: `wk`, `to`, `cancel`, `empty`, `rx`

Example shape:

```
PacketRouter recv: t=03:12.504 wait=85.1234 drain=10.4567 proc=04.4201 | wk=123 to=20 cancel=0 empty=5 rx=4567
```

Where percentages are computed from tick accumulators divided by `calTicks` in the same interval.

Reset per-interval counters and per-interval tick accumulators after each emission (calculated over the same 5s window).

---

## Implementation notes (non-optional)

- All Stopwatches and mutable accumulators must be created **once** outside the loop.
- `swCalendar` is started once and never restarted; for interval stats, keep `lastStatsTicks` and compute `intervalCalTicks = curCalTicks - lastStatsTicks`.
- Use `WaitHandle.WaitAny(waitHandles, waitTimeoutMs)` wrapped in `timed swWait ...`.
- `drainPackets` should:
  - call `ReceivePacket()` once (first packet)
  - if empty => increment `emptyWakeups` and (optionally) timed reset event
  - else => process first packet, then loop ReceivePacket() until empty, counting `packetsRx` and timing `processPacket`.
