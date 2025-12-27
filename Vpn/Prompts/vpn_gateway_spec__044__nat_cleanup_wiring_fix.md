# vpn_gateway_spec__044__nat_cleanup_wiring_fix.md

## Goal

Fix the server-side NAT exhaustion issue:

```
NAT: no free external ports/ids
```

Claude Code (CC) must implement a **periodic NAT cleanup** by wiring an existing cleanup function so NAT mappings do not accumulate indefinitely.

This task is limited to NAT cleanup wiring only.

---

## Background (What is already in the code)

CC report confirms:

- Error originates in `C:\GitHub\Softellect\Vpn\Server\Nat.fs` inside `allocateExternalPortOrId`.
- NAT has an existing cleanup function `removeStaleEntries (maxIdle: TimeSpan)` in the same file.
- **`removeStaleEntries` is never called anywhere**, so mappings accumulate forever and exhaustion is guaranteed.

---

## Hard Requirements

1. **Implement periodic invocation** of:
   - `Nat.removeStaleEntries maxIdle`
2. Add **one hardcoded “setting function”** in:
   - `C:\GitHub\Softellect\Vpn\Core\AppSettings.fs`
   This is intentionally hardcoded now; later it will be moved to `appsettings.json`.
3. Ensure the periodic cleanup:
   - runs on the server,
   - starts during server startup,
   - stops cleanly on server shutdown (cancellation token),
   - is non-blocking for packet processing loops.
4. Examine both folders as needed (some code lives in both):
   - `C:\GitHub\Softellect\Vpn\`
   - `C:\GitHub\Softellect\Apps\Vpn\`

---

## Implementation Steps (Authoritative)

### Step 1 — Add hardcoded NAT cleanup settings function

File:
- `C:\GitHub\Softellect\Vpn\Core\AppSettings.fs`

Add **one function** (single entry point) that returns both the cleanup tick interval and the max idle timeout:

- Signature (exact):
  - `val getNatCleanupSettings : unit -> TimeSpan * TimeSpan`

- Behavior:
  - returns `(cleanupPeriod, maxIdle)`

Hardcode these values (exact):
- `cleanupPeriod = TimeSpan.FromSeconds(30.0)`
- `maxIdle      = TimeSpan.FromMinutes(30.0)`

Notes:
- Keep it as a normal function (not a mutable value).
- Do not read from JSON.
- Do not add additional “config system” scaffolding.

### Step 2 — Wire periodic cleanup into the server startup

Find the server startup entry point (where long-running loops / background tasks are started). This may be in `Softellect.Vpn` server project and/or the hosting project under `C:\GitHub\Softellect\Apps\Vpn\`.

Add a small background loop/task that:

1. Loads settings once at startup:
   - `(cleanupPeriod, maxIdle) = AppSettings.getNatCleanupSettings()`
2. Runs until cancellation:
   - wait `cleanupPeriod`
   - call `Nat.removeStaleEntries maxIdle`

Hard constraints:
- Must use the application/service cancellation token used elsewhere in the server.
- Must not block the main thread.
- Must not use `Thread.Sleep`.
- Must not spam logs.
  - If you log at all, log **at most once per cleanup tick** and at **Trace/Debug** level; do not log per entry removed.

### Step 3 — Ensure cleanup is actually invoked

Add the wiring such that:
- The cleanup loop is started exactly once per server process.
- It starts early (on startup), before long-running receive loops run indefinitely.

---

## Acceptance Criteria

1. Server calls `Nat.removeStaleEntries` periodically while running.
2. On shutdown, the cleanup loop stops without throwing (respects cancellation).
3. No changes are made to NAT allocation logic besides the periodic cleanup wiring.
4. No changes to packet size / MTU / PushMtu / fragmentation logic (out of scope).
5. Code compiles.

---

## Deliverables

- Modified files implementing the above.
- Keep changes minimal and localized.
