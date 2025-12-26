# vpn_gateway_spec__036__udp_client_async_and_server_bounded_queue.md

**Goal:** eliminate control‑plane stalls/timeouts and reduce latency jitter by making UDP RPC fully async, removing client-side polling sleep, and bounding server request processing without unbounded task backlog.

**Hard constraints (do not deviate):**
- **No extra features. No refactors outside the files listed.**
- Keep behavior compatible with current wire format in `Core/UdpProtocol.fs`.
- Keep logging minimal; do not add heavy per-packet logs.
- Do not build/test. Just implement per this spec.

---

## Files in scope

1. `C:\GitHub\Softellect\Vpn\Client\UdpClient.fs`
2. `C:\GitHub\Softellect\Vpn\Server\UdpServer.fs`
3. `C:\GitHub\Softellect\Vpn\Client\Service.fs`
4. `C:\GitHub\Softellect\Vpn\Server\Service.fs`
5. `C:\GitHub\Softellect\Vpn\Core\UdpProtocol.fs`

---

## Problem summary (what we are fixing)

Current UDP “RPC” path blocks threadpool threads (`Task.Wait`) and can accumulate unbounded server worker tasks that wait on `inFlight`. Under load (speedtest/ping), this produces:
- client `ConnectionTimeoutErr`,
- high jitter (hundreds to thousands of ms),
- severe throughput collapse.

---

## Required changes

### 1) Make UDP client requests fully async (no blocking `.Wait()`)

**File:** `Client\UdpClient.fs`

#### 1.1 Add an async request function
Create an internal async function:

- `sendRequestAsync (requestMsgType: byte) (reqClientId: VpnClientId) (payload: byte[]) : Task<Result<ResponseData, VpnError>>`

Rules:
- Must allocate `requestId` as before.
- Must register `PendingRequest` with a `TaskCompletionSource<ResponseData>(TaskCreationOptions.RunContinuationsAsynchronously)`.
- Must send fragments exactly as today (same pacing behavior is OK).
- Must await completion with a hard timeout **without blocking**:
  - Use `Task.WhenAny(pending.tcs.Task, Task.Delay(hardTimeoutMs, clientCts.Token))`.
  - If timeout wins: remove pending request, return `Error (ConnectionErr ConnectionTimeoutErr)`.
- On socket exceptions: remove pending request, return `ServerUnreachableErr ...` as today.

**Important:** Do not change the receive loop protocol parsing or fragment reassembly semantics.

#### 1.2 Keep the existing `sendRequest` but turn it into a thin wrapper
Keep the existing synchronous `sendRequest` signature (because `IVpnClient` is currently sync), but implement it as:

- `sendRequest` calls `sendRequestAsync(...).GetAwaiter().GetResult()`.

**Do not** use `Task.Wait()` anywhere in UDP client after this change.

---

### 2) Remove client-side empty backoff sleep; rely on server long-poll wait

**File:** `Client\Service.fs`

In `receiveLoopAsync`:

- Remove:
  - `Thread.Sleep(ReceiveEmptyBackoffMs)` in the `| Ok None ->` branch.
- Replace it with **no sleep** (immediately continue loop).

Do not change other logic.

---

### 3) Implement real server-side wait based on ReceivePacketsRequest (maxWaitMs/maxPackets)

**File:** `Server\Service.fs`

Change server receive to honor request parameters:

- Read `maxPackets` and `maxWaitMs`.
- Clamp:
  - `maxPackets` to `[1..1024]`.
  - `maxWaitMs` to `[0..2000]` (0 means no wait).
- Implement:
  1) Attempt immediate dequeue up to `maxPackets`.
  2) If none and `maxWaitMs > 0`, wait on `session.packetsAvailable.Wait(maxWaitMs)`.
  3) Dequeue again up to `maxPackets`.
  4) Return `Ok None` if still empty, else `Ok (Some packets)`.

**Interface constraint:** do not change `IVpnService`.

Implementation approach:
- Add a new internal interface in `Server\Service.fs`:
  - `type IVpnServiceInternal = abstract ReceivePacketsWithWait : VpnClientId * int * int -> Result<byte[][] option, VpnError>`
- Make the server service implement this interface (in addition to `IVpnService`).
- Keep the existing `IVpnService.receivePackets clientId` method behavior unchanged (it can call the internal method with defaults).

---

### 4) Bound server UDP request processing using a bounded Channel (no unbounded task buildup)

**File:** `Server\UdpServer.fs`

Replace `SemaphoreSlim inFlight` + per-request Task spawn with a bounded work queue.

#### 4.1 Add bounded channel
- Add `open System.Threading.Channels`
- Define `WorkItem` as:
  - `(msgType: byte, clientId: VpnClientId, requestId: uint32, logicalPayload: byte[], remoteEp: IPEndPoint)`
- Create:
  - `let workerCount = 16` (fixed)
  - `let workChannel = Channel.CreateBounded<WorkItem>(BoundedChannelOptions(4096, FullMode = BoundedChannelFullMode.Wait, SingleWriter = true, SingleReader = false))`

#### 4.2 Start workers in StartAsync
Start `workerCount` background tasks, each:
- loops while not cancelled,
- reads `workChannel.Reader.ReadAsync(ct)`,
- calls `processRequest`,
- sends response fragments back to captured `remoteEp` (no global lock).

Remove:
- `sendLock`
- `SemaphoreSlim inFlight`

#### 4.3 Receive loop enqueues work
After complete reassembly:
- `workChannel.Writer.WriteAsync(workItem, ct)` (await it).
- Do not create per-request tasks.

---

### 5) Use ReceivePacketsRequest in UDP server to call wait-aware service

**File:** `Server\UdpServer.fs`

In `processReceivePackets`:
- Deserialize `ReceivePacketsRequest` as already done.
- If `service :? IVpnServiceInternal`, call:
  - `ReceivePacketsWithWait(clientId, req.maxWaitMs, req.maxPackets)`
- Else fallback to `service.receivePackets clientId`.

Serialize response as before.

---

## Acceptance criteria

1) Client log should stop showing repeated `ConnectionErr ConnectionTimeoutErr` under normal ping + speedtest usage.
2) Ping RTT distribution narrows significantly (no multi-second spikes during light load).
3) speedtest.net ping stabilizes and throughput improves materially versus current 1.26/0.41 Mbps.

---

## Non-goals (do not implement)

- No NAT/ExternalInterface changes.
- No protocol redesign or encryption.
- No logging expansion.

