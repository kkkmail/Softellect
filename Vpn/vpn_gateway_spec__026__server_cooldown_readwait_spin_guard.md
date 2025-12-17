# vpn_gateway_spec__026__server_cooldown_readwait_spin_guard.md
**Target:** CC (Claude Code)  
**Repo root:** `C:\GitHub\Softellect\Vpn\`  
**Goal:** Stop server burning a full core by preventing a tight spin in the WinTun receive loop when the read wait event is spuriously/permanently signaled (or otherwise causes immediate wakeups with no packets).  
**Constraints:** Do **not** increase global logging verbosity. Do **not** add heavy/per-packet logs. Prefer one-file change if possible.

---

## Symptoms / hypothesis
Server CPU burns one core even when idle. The most likely hot loop is:

- `PacketRouter.receiveLoop`:
  - `WaitHandle.WaitAny([| readEvent; cts.Token.WaitHandle |])` returns immediately repeatedly (readEvent stuck-signaled / spurious)
  - `adp.ReceivePacket()` returns `null` (no packets)
  - loop repeats immediately → tight spin → 100% of one core

We need a **cooldown guard** that guarantees blocking / yielding in the “wake but no packet” scenario.

---

## Files to change
### ✅ Change only
- `C:\GitHub\Softellect\Vpn\Server\PacketRouter.fs`

No signature changes required.

---

## Implementation requirements

### 1) Make the wait bounded
In `receiveLoop`, replace the unbounded wait:

```fs
let waitResult = WaitHandle.WaitAny(waitHandles)
```

with a bounded wait:

```fs
let waitTimeoutMs = 250
let waitResult = WaitHandle.WaitAny(waitHandles, waitTimeoutMs)
```

Then handle `WaitTimeout`:

```fs
if waitResult = WaitHandle.WaitTimeout then
    // no event fired in this period; just continue (allows loop to observe running/cts)
    ()
elif waitResult = 1 || cts.Token.IsCancellationRequested then
    ()
else
    // readEvent fired
    ...
```

### 2) Add “empty wakeup” guard (spurious/stuck signaled event)
When `waitResult` indicates `readEvent` (index 0), we start draining.

**Change it** so we detect the special case:
- we woke due to `readEvent`
- but the **first** `ReceivePacket()` is `null` or empty

In that case:
- **reset** the managed wait handle if possible (only if it’s an `EventWaitHandle`)
- rely on the bounded `WaitAny` (or optionally yield) so we don’t spin

Pseudo-structure (implement exactly in F#):

```fs
let resetReadEventIfPossible () =
    match readEvent with
    | :? EventWaitHandle as ewh ->
        try ewh.Reset() |> ignore with _ -> ()
    | _ -> ()

... inside the readEvent branch ...

let first = adp.ReceivePacket()
if isNull first || first.Length = 0 then
    // Spurious wakeup / stuck signaled: avoid tight spin
    // Reset and continue; bounded WaitAny prevents infinite CPU burn
    resetReadEventIfPossible()
else
    // process `first` as normal packet, then continue draining remaining packets
    processPacket first
    let mutable hasMore = true
    while hasMore do
        let packet = adp.ReceivePacket()
        if isNull packet || packet.Length = 0 then hasMore <- false
        else processPacket packet
```

Where `processPacket` is the existing “parse IP version → DNS proxy / ICMP / NAT / route to client” logic.
You can implement `processPacket` as a local `let processPacket (packet: byte[]) = ...` to avoid duplicating code.

### 3) Add ultra-light throttled **Info** stats (NO trace)
Add counters in `receiveLoop` scope:
- `waitWakeups`
- `emptyWakeups`
- `packetsRx`

Log at **Info** at most once per ~5 seconds (Stopwatch-based), like:

```fs
Logger.logInfo $"PacketRouter recv stats: wakeups={...}, emptyWakeups={...}, packetsRx={...}"
```

No packet dumps, no per-packet logs. This should remain low volume.

This is to confirm whether the hot loop is caused by “empty wakeups”.

---

## Acceptance criteria
1. With VPN server running but idle (no traffic), CPU no longer pins a full core.
2. With real traffic, throughput should not regress because:
   - readEvent should still wake immediately for real packets
   - only “spurious” wakeups get cooled down
3. Info-level logs:
   - One line every ~5 seconds maximum
   - Must **not** grow fast even under load

---

## Notes / non-goals
- Do not change `ExternalInterface.fs` in this step.
- Do not change global log level.
- Do not add tracing, packet dumps, or heavy logging.
- Do not refactor NAT / DNS / ICMP logic beyond extracting a local helper in `receiveLoop` if needed to avoid duplication.
