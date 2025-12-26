# vpn_gateway_spec__023__externalinterface_filter_direction_and_throttled_stats.md

## Goal

Fix server correctness + CPU by stopping `ExternalGateway` from processing **outbound/self traffic** and other irrelevant packets produced by `SIO_RCVALL`, and add **throttled Info stats** to confirm behavior without profilers.

This spec is **non-optional** and must be implemented exactly.

---

## Why this is required

On Windows with `IOControlCode.ReceiveAll`, the raw socket can receive packets that are not true “internet inbound” traffic, including packets originating from the server itself. Feeding those back into NAT inbound handling can create massive churn, break upload, and burn CPU.

We will:
- accept only packets that look like **inbound to the server public IP**
- drop packets that look like **outbound from the server public IP**
- avoid per-packet allocations for dropped packets
- log periodic counters at Info level so we can validate on the weak server without dotnet-trace

---

## Files to modify

- `C:\GitHub\Softellect\Vpn\Server\ExternalInterface.fs`

No other files unless required by compilation.

---

## Required changes

### 1) Precompute server public IP bytes

Inside `ExternalGateway(config: ExternalConfig)` add:

- `let serverIpBytes = config.serverPublicIp.GetAddressBytes()` (must be 4 bytes)

### 2) Add counters + throttled logger (Info level)

Inside `ExternalGateway` add counters using `System.Threading.Interlocked`:

- `let mutable totalReceived = 0L`
- `let mutable passedToCallback = 0L`
- `let mutable droppedTooShort = 0L`
- `let mutable droppedNotDstServerIp = 0L`
- `let mutable droppedSrcIsServerIp = 0L`
- `let mutable receiveErrors = 0L`

Add:
- `let statsStopwatch = System.Diagnostics.Stopwatch()`

Start it in `start()` when `running <- true`:
- `statsStopwatch.Restart()`

Add a function `logStatsIfDue()` called from the receive completion path:
- If `statsStopwatch.ElapsedMilliseconds >= 5000L` then:
  - capture current values (Interlocked.Read)
  - `Logger.logInfo` one line like:
    - `"ExternalGateway stats: total=..., passed=..., dropShort=..., dropNotDst=..., dropSrc=..., errors=..."`
  - `statsStopwatch.Restart()`

Do **not** use Trace logging.

### 3) Filter packets BEFORE allocating/copying

In `handleCompleted`, in the `SocketError.Success when e.BytesTransferred > 0` branch:

**DO NOT** allocate/copy immediately.

Instead:

1) `Interlocked.Increment(&totalReceived)`.

2) If `e.BytesTransferred < 20`:
   - `Interlocked.Increment(&droppedTooShort)`
   - call `logStatsIfDue()`
   - re-issue receive (`startReceive()`)
   - return (no allocation, no callback)

3) Extract src/dst IP bytes from `receiveBuffer` WITHOUT allocation:
   - src: bytes 12..15
   - dst: bytes 16..19

4) Drop if destination IP is NOT the server public IP:
   - compare `receiveBuffer[16..19]` to `serverIpBytes[0..3]`
   - if not equal:
     - `Interlocked.Increment(&droppedNotDstServerIp)`
     - call `logStatsIfDue()`
     - `startReceive()`
     - return

5) Drop if source IP IS the server public IP (outbound/self traffic):
   - compare `receiveBuffer[12..15]` to `serverIpBytes[0..3]`
   - if equal:
     - `Interlocked.Increment(&droppedSrcIsServerIp)`
     - call `logStatsIfDue()`
     - `startReceive()`
     - return

6) Only now allocate/copy:
   - allocate `packet = Array.zeroCreate<byte> e.BytesTransferred`
   - copy from `receiveBuffer` into `packet`
   - `Interlocked.Increment(&passedToCallback)`
   - invoke callback (if present)
   - call `logStatsIfDue()`
   - re-issue receive

This must be done exactly to prevent CPU burn from copying irrelevant packets.

### 4) Error handling counters

In error branches (`SocketError` not success):
- `Interlocked.Increment(&receiveErrors)` when `running = true` and this is not a shutdown error.
- call `logStatsIfDue()` before re-issuing receive (if re-issuing).

### 5) Do NOT change these behaviors

- Keep `IOControlCode.ReceiveAll` as-is for now (do not remove it in this iteration).
- Keep `sendOutbound` unchanged.
- Keep public method signatures unchanged.
- No sleeps, no delays, no polling.

---

## Acceptance criteria

After applying:

1) Server CPU at idle drops materially (no pegged core).
2) Speedtest upload is no longer stuck at exactly 0 (or at least changes meaningfully).
3) Info logs show counters changing; most packets should be dropped by `dropNotDst` and/or `dropSrc` rather than passed.
4) VPN remains functional.

---

## Output format (CC)

- Implement changes.
- Final response: list only modified files.
- No optional suggestions.
