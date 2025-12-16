# vpn_gateway_spec__021__wintun_wait_event_and_wcf_long_poll.md

## Goal

Fix the current high CPU usage and poor VPN throughput/latency by implementing **exactly** these two changes:

1) **WinTun wait-event based receive** (no busy spinning)
2) **Server-side WCF long-polling** for `receivePackets` (no tight remote polling)

There are **no optional items** in this spec.

---

## Locked decisions

- WCF long-poll timeout: **250 ms** (hard-coded)
- WinTun wait strategy: **WaitAny(readEvent, cancellationToken.WaitHandle)**
- Packet type: **`byte[]` only**
- No retries/backoff/delays added anywhere (other than the server-side long-poll wait described here)

---

## Files to modify

### Interop
- `C:\GitHub\Softellect\Vpn\Interop\WinTunAdapter.cs`

### Client
- `C:\GitHub\Softellect\Vpn\Client\Tunnel.fs`
- `C:\GitHub\Softellect\Vpn\Client\Service.fs` (only if needed to pass cancellation token / stop loops cleanly; do not add sleeps/delays)

### Server
- `C:\GitHub\Softellect\Vpn\Server\Service.fs`
- `C:\GitHub\Softellect\Vpn\Server\ClientRegistry.fs` (and any file where the per-client session record/type is defined)
- `C:\GitHub\Softellect\Vpn\Server\PacketRouter.fs`

No other files are in scope.

---

## Part 1 — WinTun wait-event based receive

### 1.1 Update `WinTunAdapter.cs`: expose a managed wait handle

Add a managed wait handle wrapper for the WinTun session read event.

**Requirements:**
- Use `WinTun.WintunGetReadWaitEvent(_session)` (already present).
- Wrap the returned handle as a `WaitHandle` without taking ownership of the underlying OS handle.

**Implementation rules (must follow):**
- Add a private field:
  - `private System.Threading.WaitHandle? _readWaitHandle;`
- When starting a session (`StartSession`), set `_readWaitHandle = null`.
- Add a public method:
  - `public System.Threading.WaitHandle? GetReadWaitHandle()`
    - If `_session == IntPtr.Zero`, return null.
    - Otherwise return a cached `WaitHandle` created from the handle returned by `WintunGetReadWaitEvent`.

**Handle wrapping detail (must be done this way):**
- Create an `EventWaitHandle` instance and assign its `SafeWaitHandle` to the native handle with `ownsHandle: false`.
- Use `Microsoft.Win32.SafeHandles.SafeWaitHandle`.

**Dispose rules:**
- In `EndSession()`:
  - dispose `_readWaitHandle` if non-null
  - set `_readWaitHandle = null`
  - then end the session as you already do

No other behavior changes in this file.

### 1.2 Update Client `Tunnel.fs`: wait then drain

Replace the current tight `while running do ReceivePacket()` loop with:

**Loop semantics (exact):**
1) Get `readEvent = adp.GetReadWaitHandle()`
   - If null: log warning once and exit the loop (do not spin)
2) While running:
   - Wait using:
     - `WaitHandle.WaitAny([| readEvent; cts.Token.WaitHandle |])`
   - If cancellation is signaled: exit loop
   - Otherwise (readEvent signaled):
     - Drain packets:
       - repeatedly call `adp.ReceivePacket()` until it returns null (or empty)
       - for each packet:
         - if IPv4: write `byte[]` to the existing outbound channel
         - if IPv6: drop
         - else: drop

**Important:**
- Do **not** call `Thread.Sleep` or `Task.Delay` anywhere.
- Keep the exact existing packet routing logic (IPv4 kept, IPv6 dropped).

### 1.3 Update Server `PacketRouter.fs`: wait then drain

Wherever the server currently reads from its WinTun adapter using `ReceivePacket()` in a tight loop, apply the same pattern as client:

- Obtain `readEvent` via `GetReadWaitHandle()`
- `WaitAny([| readEvent; cancellationToken.WaitHandle |])`
- Drain `ReceivePacket()` until null
- Route packets exactly as before

No sleeps/delays.

---

## Part 2 — Server WCF `receivePackets` long-poll

### 2.1 Add per-client “packets available” signal to the registry

In the server registry/session storage (in `ClientRegistry.fs` and/or session record/type definition):

**Add:**
- `packetsAvailable : System.Threading.SemaphoreSlim`

**Initialization:**
- When a session is created, initialize:
  - `SemaphoreSlim(0, Int32.MaxValue)`

**When enqueueing a packet for a client:**
- After the packet is enqueued successfully:
  - call `packetsAvailable.Release()` exactly once per enqueue call

### 2.2 Implement long-poll in `Server\Service.fs` `receivePackets`

Modify only the `member _.receivePackets clientId = ...` implementation.

**New behavior (exact):**
1) Validate session exists (as today). If no session → same error as today.
2) Attempt immediate dequeue:
   - `let packets = registry.dequeuePacketsForClient(clientId, 100)`
   - If packets non-empty → `Ok (Some packets)`
3) If empty:
   - Wait on the session’s semaphore for up to **250 ms**:
     - `session.packetsAvailable.Wait(250)`
   - After the wait, attempt dequeue again:
     - `let packets2 = registry.dequeuePacketsForClient(clientId, 100)`
     - If packets2 non-empty → `Ok (Some packets2)`
     - Else → `Ok None`

**Strict rules:**
- No client-side delays are introduced.
- Only the server blocks, inside the WCF method, for up to 250 ms.

---

## Acceptance criteria

1) Client and server CPU at idle drops significantly (no busy spin).
2) SpeedTest latency and throughput improve substantially.
3) No `Thread.Sleep` reintroduced.
4) WinTun receive loops do not spin when idle.
5) WCF `receivePackets` call rate decreases at idle due to server long-poll.

---

## Output requirements

- Implement exactly the changes described above.
- Do not add optional refactors or “recommendations”.
- List only the modified files in your final response.
