# vpn_gateway_spec__061__linux_tun_adapter__V01

## Purpose

Implement a **real Linux TUN adapter** and plug it into the server so that `tryGetTunAdapter` returns a working adapter on Linux (AlmaLinux 9), replacing the current `failwith ""` stub.

This is the first functional Linux step: packets must be able to flow between the Linux kernel (via `/dev/net/tun`) and the existing server routing pipeline.

## Hard constraints

1) Conditional compilation symbols:
- Allowed: `ANDROID`, `LINUX`
- Disallowed: `WINDOWS` (do not introduce this symbol anywhere)

2) F# naming:
- **Use camelCase** for any new F# identifiers.
- **Use camelCase** for any new F# identifiers.
- Do **not** rename existing identifiers even if they violate conventions.

3) Discrepancies/conflicts:
- If anything in this spec conflicts with repo reality, **STOP and ASK** before proceeding.

4) Copy/paste:
- Copy/paste is disallowed.

## Target environment

- Linux distribution: **AlmaLinux 9**
- Runtime: **.NET 10**
- Implementation is for **server** (Linux server) only.

## Current integration point (must be updated)

File:
- `C:\GitHub\Softellect\Vpn\Server\Service.fs`

Current code (conceptual):

- Under `#if LINUX`, `tryGetTunAdapter` is a stub (`failwith ""`).
- Under `#else`, it calls `Softellect.Vpn.Interop.WinTunAdapter.Create(...)`.

**Required change:**
- Under `#if LINUX`, `tryGetTunAdapter` must call the new Linux adapter creator (implemented in this spec).
- Under `#else`, keep the existing Windows behavior unchanged.

## Where to implement Linux adapter

Preferred location (F# first):
- `C:\GitHub\Softellect\Vpn\LinuxServer\`  (**existing LinuxServer project**)

If Linux TUN plumbing is significantly easier in C# (acceptable fallback):
- Create a new project:
  - `C:\GitHub\Softellect\Vpn\InteropLinux\`
- This project must contain only Linux-specific native interop code.
- The public API exposed from this project must still be consumable from F# and match the expected adapter-creator signature.

**Decision rule:**
- Implement in **F#** inside `LinuxServer` if ioctl/poll/event integration can be done cleanly.
- Otherwise implement the low-level parts in **C#** (`InteropLinux`) and keep F# as the composition/wrapper.
- If C# approach is taken, then look up C:\GitHub\Softellect\Vpn\Interop\WinTunAdapter.cs for correct mapping of F# types in C#

If uncertain ask first, implement the simplest working approach and document any limitations.

## Adapter contract

Use the existing adapter abstraction already introduced for removing `#if LINUX` from `PacketRouter`:
- The creator must return the adapter type expected by `tryGetTunAdapter`.
- Do not re-introduce direct references to `WinTunAdapter` in routing logic.

The codebase uses an interface `ITunAdapter`, so and a creator function:
- Linux implementation must provide an object implementing `ITunAdapter`. Look up the Windows version in C:\GitHub\Softellect\Vpn\Interop\WinTunAdapter.cs
- Creator signature should match the Windows one: `(name, tunnelType, guid) -> Result<ITunAdapter, string>`.

## Functional requirements (Linux)

### 1) TUN device creation
Linux adapter must create/open a TUN interface using `/dev/net/tun`:
- `open("/dev/net/tun", O_RDWR)`
- `ioctl(TUNSETIFF)` with `IFF_TUN | IFF_NO_PI`
- Interface name should be based on the `name` parameter (truncate/adjust per Linux limits if needed).

### 2) Packet I/O
- `receivePacket` must read a single packet from the TUN fd and return it.
- `sendPacket` must write a single packet to the TUN fd.

### 3) Read readiness / wait handle
The existing router loop expects a `WaitHandle` (Windows-style). Linux has fds, not handles.

Provide a Linux-compatible implementation that satisfies the existing `WaitHandle` requirement without redesigning `PacketRouter`:

Acceptable approaches (choose one):
- **Approach A (recommended minimal):**
  - Start a small background thread that `poll()`s the TUN fd for readability.
  - Signal a `ManualResetEvent` when readable.
  - `getReadWaitHandle` returns that event.
  - Reset event as appropriate after reads.
- **Approach B:**
  - Use `eventfd` and integrate it with `poll()` (more complex; only if already familiar).

### 4) Adapter session semantics
Provide a minimal, working implementation for:
- `StartSession` can initialize internal state and start the poll thread.
- `ISessionActive` reflects whether the fd is open and the poll thread is alive.

### 5) Configure IP and MTU
Provide a minimal, working implementation for:
- `SetIpAddress` (IPv4 and prefix length)
- `SetMtu`
- other members of the interface `ITunAdapter` from C:\GitHub\Softellect\Vpn\Core\Primitives.fs

For v061, it is acceptable to configure interface using the `ip` command (shell-out) if netlink is too heavy:
- `ip addr add ... dev <ifname>`
- `ip link set dev <ifname> up`
- `ip link set dev <ifname> mtu <mtu>`

If shell-out is used:
- Capture and report errors clearly.
- Keep the command invocations minimal and localized.

### 6) Dispose/cleanup
On dispose:
- stop poll thread
- close fd
- best-effort remove interface config if appropriate (optional for v061; document behavior).

## Project/solution integration

- If adding a new project (`InteropLinux`), add it to the main solution and reference it from the Linux server projects as needed.
- Keep the Windows projects unchanged.
- Do not introduce new `WINDOWS` define symbols anywhere.

## Required code change in Service.fs

Replace Linux stub:

```fsharp
#if LINUX
tryGetTunAdapter = fun _ _ _ -> failwith ""
#else
tryGetTunAdapter = fun name tunnelType guid -> Softellect.Vpn.Interop.WinTunAdapter.Create(name, tunnelType, guid)
#endif
```

with:

- Under `#if LINUX`:
  - call the Linux creator you implemented, e.g. `Softellect.Vpn.LinuxServer.TunAdapter.create(...)` (actual module path per your implementation)

- Under `#else`:
  - keep the existing WinTun call exactly as-is.

## Acceptance criteria

- On Linux, `tryGetTunAdapter` returns a working adapter (not a stub).
- Packets can be read from and written to the Linux TUN interface via the existing routing pipeline.
- No new `WINDOWS` compilation symbol exists.
- New F# identifiers are camelCase; existing naming issues are untouched.
- CC asks questions if any mismatch is found.

## Notes / Future work (not part of v061)

- Replacing shell-out `ip` calls with netlink.
- Systemd unit, capabilities (`CAP_NET_ADMIN`), and production hardening.
- Removing remaining `#if LINUX` in composition roots by moving platform selection into runtime configuration.
