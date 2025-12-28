# vpn_gateway_spec__047__android_wcf_basic_http_reuse_and_remove_tcp_auth.md

## Purpose

This spec **replaces** the “custom TCP auth/ping” direction from spec 046.

**Authoritative decision:** Android will **reuse the existing WCF auth/ping code** by calling the server over **BasicHttp (HTTP)**.  
**No custom auth protocols** (no bespoke TCP listener, no JSON length-prefix protocol).

The UDP data plane remains **unchanged** (UDP only; no TCP data plane).

This is a “friends & family” VPN; keep implementation minimal and avoid refactors.

## Repo locations (Windows)

- `C:\GitHub\Softellect\Vpn\`
- `C:\GitHub\Softellect\Apps\Vpn\`
- Android shared libraries root:
  - `C:\GitHub\Softellect\Android\`

## High-level deliverables

### 1) Remove/disable spec-046 TCP-auth work
If any of the following were added during spec 046 implementation, they must be removed (or fully disabled) and the repo returned to a clean state:
- Any **server-side TCP auth listener**
- Any **custom auth/ping protocol** code (length-prefixed JSON over TCP)
- Any **appsettings keys** related to Android TCP auth (e.g., `AndroidAuthTcpPort`, `AndroidAuthTcpListenIp`, etc.)
- Any test vectors that exist only to support the removed TCP-auth path

### 2) Add Android shared libraries (linking original sources)
You already started this approach for Sys/Core.

Keep the rule:
- Android projects **link to original source files** (only what’s needed).
- Windows-specific code is guarded by conditional compilation.
- Android-specific implementations live under `C:\GitHub\Softellect\Android\...` and are included only for Android targets.
- Keep existing namespaces for now (do not namespace-refactor).

You already have:
- `C:\GitHub\Softellect\Android\Sys\Sys.fsproj`
- `C:\GitHub\Softellect\Android\Core\Core.fsproj`

**Add now:**
- `C:\GitHub\Softellect\Android\Wcf\Wcf.fsproj`

### 3) Android app project (APK)
Keep the existing plan:
- Android app provides:
  - VpnService + TUN
  - network switching handling
  - minimal UI start/stop + status + stats
- Auth/ping uses existing WCF BasicHttp code (reused unchanged where possible).

## Non-negotiable constraints

- **No TCP data plane.** Data plane remains **UDP only**.
- **No new custom auth protocol.** Use WCF BasicHttp client to call existing service(s).
- Do not rewrite the existing WCF service contracts/types “to make them platform-friendly” unless absolutely required; instead, isolate platform differences in Android-specific shim code/projects.
- Avoid big refactors. Prefer the smallest change that compiles and works.

## Key technical decisions (frozen)

### A) Auth/ping transport
- **WCF BasicHttpBinding over HTTP** (not HTTPS for now).
- Android client must call the same server endpoints as Windows does for auth/ping (whatever the current code uses after your BasicHttp switch).

### B) Android cleartext HTTP
Because you are using HTTP (not HTTPS), the Android app must explicitly allow cleartext traffic to your server endpoint; implement the minimal Android network security config or manifest setting required.

### C) MTU
- Keep MTU = **1300** (hardcoded), same as Windows for now.

### D) Ports
- Do **not** introduce additional ports beyond what already exists:
  - BasicHttp port (auth/ping)
  - UDP port (data plane)
- Do not split UDP/WCF ports further “for clarity”. Not in this stage.

## Work plan (CC must follow in order)

### Stage 1: Read the mapping report and locate the current WCF auth flow
1) Read:
   - `vpn_gateway_spec__045__report__01.md`
   - the last spec 046 (for understanding what to remove), but **do not implement 046**
2) Identify in code:
   - The current WCF BasicHttp service contract(s) and endpoint path(s)
   - The client-side auth/ping call site(s) on Windows
   - The data structures (DTOs) used by auth/ping and what assemblies they live in

### Stage 2: Create `Softellect.Android.Wcf` project
Create:
- `C:\GitHub\Softellect\Android\Wcf\Wcf.fsproj`

Rules:
- Link only the WCF-related shared files needed by the VPN client to compile auth/ping calls.
- If a linked file contains Windows-only code (e.g., certificates store, Windows-specific networking), wrap it with conditional compilation and provide an Android implementation (or stub if not needed for BasicHttp).
- Keep API surface compatible with existing Windows call sites as much as possible.

### Stage 3: Wire Android build of the auth client (BasicHttp)
Goal: ensure the Android solution can compile and run the WCF BasicHttp auth/ping code path.

Requirements:
- Use the same service contracts / DTOs as Windows.
- Keep endpoint config driven by imported config (see below).

### Stage 4: Android app project + minimal UI
Create Android app from scratch under:
- `C:\GitHub\Softellect\Apps\Vpn\Android\`

UI requirements (minimum):
- Start/Stop button with state colors:
  - Disconnected = red
  - Connecting = yellow/orange
  - Connected = green
- Minimal stats:
  - Server host/ip
  - BasicHttp port
  - UDP port
  - Bytes sent/received (UDP)
  - SessionId (shortened) if available

### Stage 5: VpnService + UDP data plane reuse
- Implement VpnService TUN pump loops:
  - TUN → UDP encode/encrypt (existing format) → send to server
  - UDP receive → decode/decrypt → write to TUN
- Implement network switching:
  - On network change, rebind sockets, re-run WCF auth/ping if needed, resume UDP

### Stage 6: Manual config + keys provisioning (simplest)
Use a single imported JSON config file (file picker or ADB copy) stored in app-private storage.

Must include (minimum):
- `serverHost`
- `basicHttpPort`
- `udpPort`
- `clientId`
- any existing key material required by UDP plane and/or auth

Keystore is out of scope for this stage.

## Removal checklist (must be explicit in commit message)
If CC already implemented parts of spec 046, ensure these are removed:
- server TCP listener for Android auth
- TCP auth protocol message types
- Android TCP auth settings in appsettings
- TCP-auth-specific test vectors
- any documentation that claims Android uses custom auth protocol

## Acceptance criteria

- Windows build continues working unchanged.
- Server hosts BasicHttp auth/ping as before.
- Android app can:
  - import config
  - start VPN (shows Connecting → Connected)
  - call WCF BasicHttp auth/ping successfully (log success)
  - start UDP tunnel loops (even if first tests are limited to emulator/BlueStacks)
  - show minimal stats updating (bytes in/out)

---
End of spec 047.
