# vpn_gateway_spec__028__answers__02.md
## CC question: drainPackets timing structure (drain vs process overlap)

Use **Option B**: **do not overlap** `drainTicks` with `processTicks`.

Reason: the log line is intended to be read as a breakdown of where time goes (relative to `calendarTicks`). If `drain` includes `process`, the reported percentages can exceed 100% (because of overlap), which makes the “is it spinning?” diagnosis harder.

### Required semantics
- `waitTicks`: time spent inside `WaitHandle.WaitAny(...)`
- `drainTicks`: time spent **pulling packets out of WinTun** and doing drain-loop control, i.e. primarily `ReceivePacket()` calls (which include the copy from unmanaged memory), plus the loop overhead. **Exclude** `processPacket` work.
- `processTicks`: time spent inside `processPacket` (routing / NAT / DNS / ICMP logic).

### How to implement Option B cleanly (without per-packet `timed` calls)
Inside `drainPackets`, do this pattern:
1. Start `swDrain`, call `ReceivePacket()`, stop `swDrain`.
2. If empty: increment `emptyWakeups`; optionally reset event (not bucketed).
3. If non-empty:
   - `timed swProcess ... (fun () -> processPacket first)`
   - loop:
     - start `swDrain`, call `ReceivePacket()`, stop `swDrain`
     - if empty => break
     - else `timed swProcess ... (fun () -> processPacket packet)`

This keeps:
- `drainCount` = number of `ReceivePacket()` calls (including the final empty one if you want; either is fine—just be consistent)
- `processCount` = number of packets actually processed

### One minor note about interpretation
With this definition, **high `drain%`** means you spend time in `ReceivePacket()` (including the underlying marshal/copy) and the drain loop, **not** in packet routing logic. That matches the intent of the instrumentation.
