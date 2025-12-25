# vpn_gateway_spec__022__packetrouter_receiveLoop_refactor_with_timers.md

## Context / Goal

The server still burns ~1 CPU core. We need to determine whether `PacketRouter.receiveLoop` is **actually waiting** (blocking) or **spinning** (waking without real work), and at the same time make the code readable and testable.

This task refactors **only the server-side `PacketRouter.receiveLoop`** to:
- Extract almost all “happy path” logic into small helper functions **outside** the loop.
- Add **low-volume `logInfo` stats** (throttled) that include:
  - Calendar elapsed time.
  - Time spent in key buckets (as **percent of calendar**).
  - Key counters (wakeups, timeouts, packets, etc.).
- Add a `timed` helper that measures elapsed time for an action and updates:
  - **time accumulator**
  - **count accumulator**
- Use **percentage format `00.0000`** (two digits, dot, four digits).

Do **NOT** change log level. Do **NOT** add trace spam. The new info logging must remain small and throttled.

## Files to modify

- `C:\GitHub\Softellect\Vpn\Server\PacketRouter.fs`

No other files unless strictly necessary for compilation (should not be necessary).

## Non-goals

- Do not redesign NAT/DNS/ICMP behavior.
- Do not change semantics of routing decisions.
- Do not touch WCF/service code.
- Do not “optimize” by adding more threads/async, etc. This task is purely about **refactor + measurement**.

## Key idea

If `t_wait / t_calendar` is **small**, the loop is **spinning**.
If `t_wait / t_calendar` is **large**, the loop is mostly **blocking**, and CPU burn is likely elsewhere (e.g., packet processing path).

We need accurate, throttled, info-level stats to prove this.

## Refactor requirements

### 1) Extract helpers outside the loop

Inside `PacketRouter` type, move these into `let` bindings **outside** `receiveLoop` (but still inside the type where they can close over `registry`, `externalGateway`, `vpnSubnetUint`, etc.):

- `resetReadEventIfPossible`
- `logStatsIfNeeded`
- `processPacket`
- A new helper to drain packets, e.g.:
  - `drainPackets adp processPacket` (or similar)

The **while loop** inside `receiveLoop` must become minimal and readable. Target shape:

- Wait (timed)
- Handle WaitAny result:
  - Timeout => stats + continue
  - Cancel => exit
  - readEvent => drain packets + stats

### 2) Timers created once; mutables outside the loop

All `Stopwatch` instances must be created **once** (outside the loop). All time accumulators and counters must also be declared outside the loop and only updated inside.

There must be a **calendar stopwatch** started once at loop start.

### 3) Add `timed` helper (time + count)

Add a helper function:

`timed (timer: Stopwatch) (timeAcc: int64 byref) (countAcc: int64 byref) (action: unit -> 'T) : 'T`

Behavior:
- `timer.Restart()` (or `Reset(); Start()`).
- Execute `action()`.
- Stop timer.
- Add `timer.ElapsedTicks` (ticks preferred) into `timeAcc`.
- Increment `countAcc` by 1.
- Return the action result.

Important: **Do not use try/with or try/finally** inside `timed` for now, as requested. (If an exception happens, timing may be lost for that call; acceptable.)

### 4) Measure wait time explicitly

Wrap `WaitHandle.WaitAny(waitHandles, waitTimeoutMs)` in `timed` using dedicated:
- `waitTimer` stopwatch instance
- `t_wait` accumulator
- `c_wait` counter

This is the most important metric.

### 5) Measure key buckets

At minimum, measure these buckets (each has timer + time accumulator + count accumulator):

- `calendar` (Stopwatch only; it is the denominator, not a bucket)
- `wait` (`WaitAny`)
- `drain` (time spent draining packets after wakeup)
- `processPacket` (time spent inside packet processing function)

Optional (only if easy and clean without exploding code):
- `resetReadEvent` time/cnt
- `dnsProxy` time/cnt (around `forwardDnsQuery` path)
- `natOutbound` time/cnt (around `translateOutbound` + `sendOutbound`)
- `icmpOutbound` time/cnt (around ICMP proxy outbound handling)

Do not add lots of micro-buckets if it makes the refactor messy. The goal is to identify the spin first.

### 6) Counters to include

Include these counters in stats logs (reset each interval):

- `waitWakeups` : number of times `waitResult` indicated readEvent (index 0)
- `waitTimeouts` : number of timeouts (`WaitHandle.WaitTimeout`)
- `waitCancels` : number of times cancellation was observed (usually 0 or 1)
- `emptyWakeups` : readEvent signaled but first `ReceivePacket()` returned null/0
- `packetsRx` : total packets processed (count of calls to `processPacket`)

Additionally, for each timed bucket you will have `c_*` and `t_*` already—those should also be logged.

### 7) Logging throttle and format

- Log at **Info** level every **5 seconds**.
- The log message must include:
  - Calendar time as `MM:SS.fff` (minutes, seconds, milliseconds).
  - Percentages for each bucket as `00.0000` (two digits, dot, four digits).
  - Key counters.

Percent definition for a bucket:
- `pct = 100.0 * (bucketTicks / calendarTicks)`
- If calendarTicks is 0, treat pct as 0.

Formatting:
- Use something like `pct.ToString("00.0000")`.
- Calendar string `mm:ss.fff` (Stopwatch elapsed).

Example shape (exact wording not required, but must be compact and readable):

`PacketRouter recv stats [01:23.456] wait=95.1234% drain=02.3456% proc=01.2345% | wakeups=123 timeouts=456 empty=7 pkts=890 | c_wait=... c_drain=... c_proc=...`

Do **not** log per-packet or per-wakeup at Info.

### 8) Reset semantics

Stats must be **per-interval**, not cumulative:
- After each throttled log emission, reset:
  - interval counters (wakeups/timeouts/empty/packets)
  - bucket accumulators (t_* and c_*)
- Calendar stopwatch must keep running (do not restart it); use a separate throttle stopwatch or last-log timestamp.

### 9) Keep correctness

All existing packet logic in `processPacket` must remain behaviorally identical:
- DNS proxy handling
- ICMP proxy outbound handling
- NAT outbound handling
- Routing to VPN clients inside subnet

Do not change decision ordering.

## Acceptance criteria

1. Build succeeds.
2. `receiveLoop` is clean and short; most logic moved into helpers.
3. Server runs and VPN still works as before.
4. Every ~5 seconds, **one** Info log line appears with:
   - calendar time `MM:SS.fff`
   - bucket percentages `00.0000`
   - key counters
5. From logs, we can diagnose:
   - spin (low wait%)
   - heavy processing (high process% / drain%)
   - spurious wakeups (high emptyWakeups / wakeups)

## Notes

- `WaitHandle.WaitAny` indices:
  - `waitHandles = [| readEvent; cts.Token.WaitHandle |]`
  - `0` = readEvent
  - `1` = cancellation token
  - `WaitHandle.WaitTimeout` means timeout
- Keep `waitTimeoutMs = 250` as currently.
