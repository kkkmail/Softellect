# vpn_gateway_spec__025__externalgateway_fix_sync_completion_spin.md

## Goal

Fix server unresponsiveness (client cannot even authenticate) and the “one-core burn” by preventing `ExternalGateway`’s receive pump from spinning on synchronous completions and/or zero-byte receives.

This spec is **non-optional**. Implement exactly. No other changes.

---

## File to modify

- `C:\GitHub\Softellect\Vpn\Server\ExternalInterface.fs`

---

## Locked changes

### A) Treat `SocketError.Success` with `BytesTransferred = 0` as terminal (do NOT re-arm)

In `handleCompleted`:

- Current behavior:
  - `| SocketError.Success -> startReceive()`

Replace with:

- If `e.SocketError = SocketError.Success` AND `e.BytesTransferred = 0`:
  - **DO NOT** call `startReceive()`
  - stop the pump by setting `running <- false`
  - return (no further work)

No logging required for this branch.

Rationale: re-arming on 0 bytes can create an infinite synchronous-completion loop on Windows raw sockets and starve WCF.

### B) Break inline recursion: never call `handleCompleted` inline from `startReceive`, and never call `startReceive` inline from `handleCompleted`

We must ensure that if `ReceiveAsync` completes synchronously (returns `false`), we do not immediately recurse into `handleCompleted` and then re-arm inline again.

Implement this rule with exactly one threadpool hop.

#### B1) Add helper `queue` inside `ExternalGateway`

Add:

- `let queue (f: unit -> unit) = System.Threading.ThreadPool.UnsafeQueueUserWorkItem((fun _ -> f()), null) |> ignore`

(Use `ThreadPool.QueueUserWorkItem` if `UnsafeQueueUserWorkItem` is not available; prefer `UnsafeQueueUserWorkItem`.)

#### B2) In `startReceive()`

Current code:

- `let pending = rawSocket.ReceiveAsync(args)`
- `if not pending then handleCompleted args`

Replace with:

- `if not pending then queue (fun () -> handleCompleted args)`

No other behavior change.

#### B3) In `handleCompleted`

For all branches where you currently call `startReceive()` (i.e. bytes>0 success branch, and non-shutdown error branch where you re-issue), replace those inline calls with queued re-issue:

- `queue startReceive`

Do NOT queue more than once per completion.

#### B4) Completed event handler

Keep `Completed` event handler as-is; it calls `handleCompleted` for async completions. Inside `handleCompleted`, re-arming must be queued per the rules above.

---

## Explicit “do not” list

- Do not add `Task.Delay`, `Thread.Sleep`, or polling.
- Do not add cancellation tokens or change public method signatures.
- Do not change `sendOutbound`.
- Do not modify `PacketRouter.fs` or any other files in this step.
- Do not add new logging beyond what already exists (except if needed for compilation).

---

## Acceptance criteria

1) Server becomes responsive to client authentication.
2) Server no longer burns a full core at idle.
3) VPN can at least connect and pass basic traffic again (performance tuning comes later).

---

## Output format (CC)

- Implement changes.
- Final response: list only modified files.
- No optional suggestions.
