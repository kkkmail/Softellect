# vpn_gateway_spec__022__externalinterface_event_driven_socket_receive.md

## Purpose

Eliminate the server-side “one core busy” behavior by rewriting the raw-socket receive path in **event-driven** form using `SocketAsyncEventArgs` (IO completion), instead of the current `Poll + while running` loop.

This change must:
- remove the busy loop behavior
- keep the external gateway behavior correct
- avoid introducing new optional knobs, delays, or polling

---

## Locked decisions

1) Use **`SocketAsyncEventArgs` + `Completed` event** for receiving.
2) Do **not** introduce cancellation tokens into public APIs.
3) Stop logic is implemented by:
   - setting `running <- false`
   - closing/disposing the socket
   - ensuring the receive pump exits cleanly while swallowing expected shutdown exceptions
4) Keep the public surface as-is unless a change is strictly required by compilation.
   - Goal is to keep `ExternalGateway.start(callback)`, `sendOutbound(packet)`, and `stop()` unchanged.

---

## Files to modify

- `C:\GitHub\Softellect\Vpn\Server\ExternalInterface.fs`

If compilation requires touching any other file, keep changes strictly minimal and list them.

---

## Required refactor in `Server\ExternalInterface.fs`

### 1) Remove the polling receive thread

Remove:
- `rawSocket.Poll(...)`
- the `while running do ...` polling loop in `receiveLoop`
- the dedicated receive thread (`receiveThread`) and its creation

There must be **no thread** whose only job is to poll `Poll()` in a loop.

### 2) Implement a single receive “pump” using `SocketAsyncEventArgs`

#### 2.1 State to add (inside `ExternalGateway`)

Add these fields (names can differ but semantics must match):

- `let mutable running = false` (already exists)
- `let mutable onPacketCallback : (byte[] -> unit) option = None` (already exists)
- `let mutable receiveArgs : SocketAsyncEventArgs option = None`
- `let receiveBuffer : byte[] = Array.zeroCreate<byte> 65535`

No additional receive buffers or per-receive allocations are allowed **except** trimming the received packet into a new `byte[]` of exact length.

#### 2.2 Completed handler (exact semantics)

Create ONE handler for `SocketAsyncEventArgs.Completed` which:

1) Immediately exits if `running` is false.
2) Checks `e.SocketError`:
   - If success AND `e.BytesTransferred > 0`:
     - copy exactly the received bytes into a new `byte[]` of length `BytesTransferred`
     - invoke `onPacketCallback` if present
   - If error is expected due to shutdown (e.g. `OperationAborted`, `Interrupted`, `NotSocket`, `ConnectionReset` etc.):
     - if `running` is false, just exit without logging
     - if `running` is true, log an error
3) Re-issue the next receive by calling the same “start receive” function again (see below), but only if `running` is still true.

Do not call `Poll()`. Do not sleep. Do not add delays.

#### 2.3 Function to start the next receive

Implement a function (internal/private) that starts the next receive:

- Calls `rawSocket.ReceiveAsync(e)`
- If `ReceiveAsync` returns `false`, it means completion occurred synchronously:
  - call the completed handler logic immediately (same code path)

This function must be used:
- once at `start()`
- after every completion (async or sync)

### 3) `start(onPacketFromInternet)` must initialize the receive pump

In `ExternalGateway.start(...)`:

1) Store callback in `onPacketCallback`.
2) Set `running <- true`.
3) Create and configure `SocketAsyncEventArgs` once:
   - `SetBuffer(receiveBuffer, 0, receiveBuffer.Length)`
   - hook up the `Completed` event to the handler
   - store it in `receiveArgs`
4) Start the receive pump by calling the “start receive” function once.

No thread creation. No `ThreadStart`. No `Poll`.

### 4) `stop()` must terminate the pump

In `stop()`:

1) Set `running <- false`
2) Clear `onPacketCallback <- None`
3) Dispose / close the socket (`rawSocket.Close()` then `rawSocket.Dispose()`), inside try/catch.
4) Dispose `SocketAsyncEventArgs` if created, and set `receiveArgs <- None`

The completed handler must tolerate the race where the socket is disposed while a receive is pending; it must not keep spinning or re-issuing receives when `running` is false.

### 5) Do not change `sendOutbound` semantics

`sendOutbound(packet)` must continue to send the full IPv4 packet as before.

No behavior changes here other than any minimal error handling changes needed due to removal of `Poll`/thread.

---

## Logging rules (locked)

- Do not add new “debug mode” branches.
- Do not add new config flags.
- Log errors only when it indicates a real fault while `running = true`.
- Expected stop/shutdown exceptions must not spam logs.

---

## Acceptance criteria

1) Server idle CPU usage drops substantially (no pegged core).
2) VPN still functions.
3) No `Poll()` usage remains in `ExternalInterface.fs`.
4) No `Thread.Sleep` or `Task.Delay` added.
5) No background receive thread exists; receives are driven by socket completion callbacks.

---

## Output format (CC)

- Implement the change.
- In final response, list only modified files.
- Do not propose optional improvements.
