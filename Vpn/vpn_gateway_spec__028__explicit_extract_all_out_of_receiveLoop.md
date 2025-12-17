# vpn_gateway_spec__028__explicit_extract_all_out_of_receiveLoop.md

## Goal

CC must **finish the refactor** of `C:\GitHub\Softellect\Vpn\Server\PacketRouter.fs` so that `receiveLoop` becomes a **thin orchestration loop** and **does not define any helper functions, timers, or mutable storages** inside itself.

Right now `receiveLoop` still contains:
- stopwatch creation (`swCalendar`, `swWait`, `swDrain`, `swProcess`)
- tick/count mutable storages (`lastStatsTicks`, `waitTicks`, `drainTicks`, `processTicks`, `waitCount`, …)
- counters (`waitWakeups`, `waitTimeouts`, `waitCancels`, `emptyWakeups`, `packetsRx`)
- helper functions (`formatPct`, `logStatsIfNeeded`, `drainPackets`)

This is **NOT acceptable**. All of that must be extracted out of `receiveLoop`.

## Hard requirements

### 1) `receiveLoop` must not contain `let` bindings except minimal plumbing
Allowed inside `receiveLoop`:
- reading `adapter` / `adp`
- retrieving `readEvent`
- building `waitHandles`
- `let st = mkReceiveLoopState()` (state creation)
- the `while` loop itself
- calling already-defined helpers

Not allowed inside `receiveLoop`:
- **any** helper definitions (no `formatPct`, no `logStatsIfNeeded`, no `drainPackets`, etc.)
- **any** stopwatch creation
- **any** mutable counters/accumulators
- **any** per-interval state initialization

### 2) Create a single state record that owns ALL timers + mutable storages
Create a private type (record/class) that carries everything needed by the loop:

Stopwatches:
- `swCalendar` (running)
- `swWait`
- `swDrain`
- `swProcess`

Per-interval tick accumulators:
- `lastStatsTicks`
- `waitTicks`
- `drainTicks`
- `processTicks`

Per-interval count accumulators:
- `waitCount`
- `drainCount`
- `processCount`

Per-interval counters:
- `waitWakeups`
- `waitTimeouts`
- `waitCancels`
- `emptyWakeups`
- `packetsRx`

Constants:
- `statsIntervalMs` = 5000L
- `waitTimeoutMs` = 250

This state is created **once per receiveLoop invocation**, by calling `mkReceiveLoopState()`.

### 3) Extract helpers as `let` bindings outside `receiveLoop` (but inside `type PacketRouter`)
Keep them as `let` bindings in the type so they can close over `registry`, `externalGateway`, NAT vars, etc.

Required helper functions (names can match; signatures must be reusable):

- `formatPct : bucketTicks:int64 -> calTicks:int64 -> string`
- `resetInterval : st:ReceiveLoopState -> curCalTicks:int64 -> unit`
- `logStatsIfNeeded : st:ReceiveLoopState -> unit`
- `waitForEvent : st:ReceiveLoopState -> waitHandles:WaitHandle[] -> int`
- `drainPackets : st:ReceiveLoopState -> adp:WinTunAdapter -> readEvent:WaitHandle -> unit`
- `handleWaitResult : st:ReceiveLoopState -> adp:WinTunAdapter -> readEvent:WaitHandle -> waitHandles:WaitHandle[] -> waitResult:int -> unit`

Target shape of `receiveLoop`:

```fs
let st = mkReceiveLoopState()
while running && not cts.Token.IsCancellationRequested do
    try
        let waitResult = waitForEvent st waitHandles
        handleWaitResult st adp readEvent waitHandles waitResult
    with ex ->
        Logger.logError ...
```

### 4) Use `timed` consistently (ticks + count)
All buckets must be accumulated via `timed`:
- `waitForEvent` wraps `WaitAny`
- each `ReceivePacket()` call in `drainPackets`
- each `processPacket` call

No ad-hoc `sw.Restart()/Stop()` in helpers unless it is done through `timed`.

### 5) Keep buckets/counters minimal (do NOT add optional buckets)
Only:
- buckets: `wait`, `drain`, `proc` (+ calendar implicit)
- counters: `wk`, `to`, `cancel`, `empty`, `rx`

### 6) Logging format stays the same
Every ~5 seconds:
- calendar time `MM:SS.fff`
- percentages formatted as `00.0000` (relative to interval calendar ticks)
- counters in one line

Example:
`PacketRouter recv: t=00:12.345 wait=99.1234 drain=00.4321 proc=00.1234 | wk=... to=... cancel=... empty=... rx=...`

### 7) Interval reset happens only in one place
After logging:
- reset all per-interval ticks, counts, counters
- update `lastStatsTicks`

## Concrete change list

1. Add a private state type near the top of the module (or as nested type inside PacketRouter):
   - `type ReceiveLoopState = { mutable ... }`

2. Add `let mkReceiveLoopState () = ...` outside `receiveLoop`:
   - create all stopwatches once
   - start `swCalendar` here
   - init all accumulators/counters to 0

3. Move `formatPct` out of `receiveLoop` (pure function).

4. Move `logStatsIfNeeded` out of `receiveLoop`:
   - uses `st.swCalendar.ElapsedTicks` and `st.lastStatsTicks`
   - logs and calls `resetInterval`

5. Move `drainPackets` out of `receiveLoop`:
   - uses `timed st.swDrain &st.drainTicks &st.drainCount (fun () -> adp.ReceivePacket())`
   - empty-first: increment `st.emptyWakeups`; reset readEvent
   - packet: increment `st.packetsRx`; timed `processPacket`; loop remaining

6. Make `receiveLoop` only orchestrate:
   - create state
   - set up wait handles
   - call helpers

7. Modify **only** `PacketRouter.fs` and keep routing logic identical.

## Acceptance checklist

- [ ] `receiveLoop` has no helper definitions and no stopwatch/counter initialization (besides `st = mkReceiveLoopState()`)
- [ ] All stopwatches and mutable storages live in `ReceiveLoopState`
- [ ] `timed` used for WaitAny + ReceivePacket + processPacket
- [ ] Minimal buckets/counters only
- [ ] Log format unchanged
- [ ] Compiles

## Deliverable

- Update `C:\GitHub\Softellect\Vpn\Server\PacketRouter.fs`
- Provide full updated file content
