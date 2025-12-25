# vpn_gateway_spec__035__udp_server_nonblocking_receive_and_remove_inline_longpoll.md

NNN = 035

## Goal

Fix UDP tunnel latency spikes / client timeouts by ensuring the UDP server **never blocks its socket receive loop** on request processing (especially `receivePackets`).

Current behavior: `UdpServer.receiveLoop` processes requests inline, and `processReceivePackets` can block for up to `maxWaitMs` via a polling loop. This stalls the only thread that calls `UdpClient.Receive`, causing OS receive-buffer buildup, drops, fragment loss, and client `ConnectionTimeoutErr`.

## Scope

**Only change:**
- `C:\GitHub\Softellect\Vpn\Server\UdpServer.fs`

Do **not** modify other files unless compilation requires it.

## Required Changes

### 1) Remove inline long-poll loop from `processReceivePackets`

In `UdpServer.fs`, rewrite `processReceivePackets` so it does **one** call to the service and returns immediately:

- Deserialize `ReceivePacketsRequest` as it does now (keep it for future compatibility), but do **not** loop/sleep/poll in UDP server.
- Call:
  - `let result = service.receivePackets clientId`
- Serialize `result` and return fragments.
- **Delete**: `PollSleepMs`, `Stopwatch` loop, repeated `service.receivePackets`, and `Thread.Sleep(PollSleepMs)`.

Reason: long-polling belongs in the service layer (or client), but blocking the UDP socket receive loop is catastrophic. The `Service.receivePackets` already does a bounded wait (`SemaphoreSlim.Wait(250)`), and that is acceptable only if it runs off the socket thread.

### 2) Make UDP receive loop non-blocking by offloading processing to worker tasks

In `receiveLoop` (the one that calls `client.Receive(&remoteEp)`):

- Keep parsing/reassembly logic on the socket thread.
- When a **full logical payload** is available (either single fragment or reassembled), do **NOT** call `processRequest` inline.
- Instead, dispatch a background task that does:
  1) update `endpointMap[clientId] <- remoteEp`
  2) `let responseFragments = processRequest msgType clientId requestId logicalPayload`
  3) send all fragments to `remoteEp`

Implementation details:

- Add near the top of `VpnUdpHostedService`:
  - `let sendLock = obj()` to serialize `client.Send(...)` calls (avoid concurrent sends on the same UDP socket).
  - `let maxInFlight = 128`
  - `let inFlight = new SemaphoreSlim(maxInFlight, maxInFlight)`

- In the dispatch task:
  - `let! _ = inFlight.WaitAsync(ct)` inside `task {}` (or equivalent)
  - `try ... finally inFlight.Release() |> ignore`
  - Wrap the whole body in `try/with` so exceptions are logged but do not kill the receive loop.

- When sending fragments inside the worker:
  - `lock sendLock (fun () -> for fragment in responseFragments do client.Send(fragment, fragment.Length, remoteEp) |> ignore)`

Notes:
- `remoteEp` must be captured per request (do not reuse a mutable variable outside).
- Do not block the socket thread waiting for `inFlight`. The `WaitAsync` happens inside the worker task, not in the socket loop.
- If `ct` is cancelled, the worker should exit quickly.

### 3) Keep cleanup behavior unchanged

- Keep `cleanupReassemblies()` calls as-is (on each receive and on timeout).
- Do not introduce additional sleeps into the socket receive loop.

## Acceptance Criteria

- `UdpServer.receiveLoop` should do only:
  - Receive datagram
  - Parse fragment header
  - Reassembly bookkeeping
  - Queue a worker when a logical request is complete
  - Cleanup stale reassemblies
- There must be **no** long-poll loop in `UdpServer`.
- There must be a clear concurrency cap for worker tasks (SemaphoreSlim with `maxInFlight = 128`).
- There must be a send lock around UDP sends.
- Code compiles.

## Sanity check after change (manual)

- Run VPN and `ping -n 20 8.8.8.8`.
- Expect:
  - No client `ConnectionTimeoutErr` spam under normal load.
  - RTT noticeably more stable (still may vary due to NAT/raw socket path, but should not show receive-loop stalls).
