# vpn_gateway_spec__034__udp_burst_control_and_client_backoff.md

## Goal

Stabilize UDP tunnel latency and eliminate periodic client timeouts by preventing **traffic bursts** and **hot-loop polling** that overload the UDP path.

Observed after fragmentation work:
- Ping RTT is no longer pinned at ~1100ms, but now shows **large oscillations** and can trend upward.
- Client logs show bursts of:
  - `Failed to send ... ConnectionErr ConnectionTimeoutErr`
  - `Failed to receive packets ... ConnectionErr ConnectionTimeoutErr`

Server UDP loop shows no obvious errors, which is consistent with **UDP burst loss / queueing / client-side overload** rather than a single server exception.

This spec is authoritative (no alternatives). Implement exactly as written.

## Root causes to fix

1) **Unbounded batching in client sendLoop**
`Service.sendLoopAsync` drains the entire outbound channel (`TryRead` in a loop) and sends all packets as one `vpnClient.sendPackets` call.
- Under load, this can create very large serialized payloads → many UDP fragments.
- Fragments are sent back-to-back → burst loss/queueing → request timeouts.
- Once timeouts start, the system can enter a “retry pressure” regime and RTT oscillates.

2) **receiveLoop hot-loop on Ok None**
`Service.receiveLoopAsync` immediately calls `vpnClient.receivePackets` again when it gets `Ok None`.
- With UDP, this creates a tight request loop if the server returns quickly or if the client processes responses faster than intended.
- This increases baseline tunnel traffic and competes with real traffic (ICMP/TCP), increasing jitter and RTT.

3) **No pacing inside multi-fragment sends**
Client sends all fragments in a tight loop with no yielding. Even with fragmentation, this can overwhelm NIC/OS buffers and upstream NAT/ISP queues.

## Required changes (only these files)

- `C:\GitHub\Softellect\Vpn\Client\Service.fs`
- `C:\GitHub\Softellect\Vpn\Client\UdpClient.fs`
- `C:\GitHub\Softellect\Vpn\Core\UdpProtocol.fs` (constants only if required)

No other files.

---

## A) Add strict client-side batching limits (Service.fs)

### A.1 Constants (add near the top of Service module)

Add these literals:

- `MaxSendPacketsPerCall = 64`
- `MaxSendBytesPerCall = 65536`  (64 KiB)
- `ReceiveEmptyBackoffMs = 10`   (10ms)

These are fixed values (do not make configurable in this change).

### A.2 Update sendLoopAsync batching logic

Current behavior:
- Reads one packet via `ReadAsync`
- Drains all remaining available packets via `TryRead`
- Sends them all in one `vpnClient.sendPackets` call

Required behavior:
- Read the first packet via `ReadAsync` as today
- Then drain additional packets, but **stop** when either limit is reached:
  - packet count reaches `MaxSendPacketsPerCall`, OR
  - total bytes would exceed `MaxSendBytesPerCall`
- If the next packet would exceed the byte limit, do **not** consume it (leave it in the channel).
  (To do this with `ChannelReader`, you must stop draining once the current packet would exceed the limit; do not call `TryRead` again. This is acceptable; the packet remains for the next loop iteration.)

Additional required behavior:
- If a send fails with `ConnectionTimeoutErr`, do **not** immediately re-send the same batch. Just log as today and continue the loop (existing behavior).

This prevents massive bursts and reduces UDP fragment trains.

### A.3 Update receiveLoopAsync behavior on Ok None

Current behavior:
- On `Ok None` it immediately loops and calls `receivePackets` again (tight loop).

Required behavior:
- On `Ok None`, execute `Thread.Sleep(ReceiveEmptyBackoffMs)` before continuing.

This is a fixed 10ms backoff to prevent busy polling and reduce jitter.

Do NOT add any other sleeps/backoffs in other branches.

---

## B) Add pacing/yield inside UDP fragment sends (UdpClient.fs)

### B.1 Add a constant

In `UdpClient.fs` (module scope), define:

- `FragmentsYieldEvery = 32`

### B.2 Modify sendRequest fragment send loop

Current behavior:
- `for fragment in fragments do udpClient.Send(fragment, fragment.Length) |> ignore`

Required behavior:
- Send fragments sequentially as today.
- After each `FragmentsYieldEvery` fragments sent, call `Thread.Yield()` once.

Exact rule:
- Maintain an integer counter while iterating fragments.
- After sending fragment number 32, 64, 96, ... call `Thread.Yield()`.

Do NOT sleep. Yield only.

This reduces OS buffer pressure without imposing fixed latency.

---

## C) Reduce fragment allocation overhead for large payloads (UdpProtocol.fs)

This is required because current fragmentation uses `Array.sub` per fragment, which allocates a new array each time.
Under sustained load, this increases GC pressure and can amplify latency oscillations.

### C.1 Change buildFragments implementation

In `UdpProtocol.fs`, modify `buildFragments` so that it does NOT allocate `fragmentPayload` via `Array.sub`.

Required approach:
- Allocate the final datagram array for each fragment (as already done in `buildFragmentDatagram`).
- Copy the fragment slice directly from the original `payload` into the datagram buffer using `Array.Copy(payload, offset, result, fullHeaderSize, length)`.
- This requires either:
  - adding a new helper `buildFragmentDatagramInto` that creates the datagram buffer and copies from the source payload, OR
  - rewriting `buildFragments` to build the full datagram buffer inline (recommended for minimal changes).

Important:
- The wire format MUST remain identical.
- The fragment payload bytes must be copied exactly from the original payload slice.

This change eliminates per-fragment payload array allocations.

---

## Acceptance criteria

1) Ping RTT is no longer progressively ramping during a 12-ping run (`ping -n 12 8.8.8.8`).
   - Some jitter is acceptable, but the “upward drift then timeouts” pattern must be gone.

2) Client logs no longer show repeated bursts of `ConnectionTimeoutErr` during idle/ping-only testing.

3) HTTPS browsing does not stall/die under normal use (basic page loads).

---

## Implementation constraints

- Do not change protocol layout, message types, request/response semantics, or timeouts in this step.
- Do not add new threads, new background loops, or new dependencies.
- Keep logging as-is except where necessary for compile errors due to refactors.
