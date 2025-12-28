# vpn_gateway_spec__046__android_auth_tcp_and_apk.md

## Scope

Stage 2 implementation based on `vpn_gateway_spec__045__report__01.md`.

We will:
1) Add a **new TCP auth/ping path** for Android clients (Android app → Windows server).
2) Add a **new Android client APK** using **VpnService + TUN**, reusing the **existing UDP data plane** unchanged.
3) Add **test vectors** to validate cross-platform crypto/framing quickly.
4) Use **MTU = 1300** on Android (same as Windows; hardcoded for now).
5) Use the **simplest manual key/config provisioning**.
6) Add minimal **Android UI**: Start/Stop button with state colors + minimal stats.

### Hard constraints
- **NO TCP data plane.** Data plane remains **UDP only**.
- **Do not change UDP wire format** (framing, scrambling, crypto, session identifiers).
- Keep changes minimal and localized; avoid “cleanup refactors”.

## Codebase locations (Windows)
- `C:\GitHub\Softellect\Vpn\`
- `C:\GitHub\Softellect\Apps\Vpn\`

## Deliverables (what CC must commit)

### A) Server-side
- New TCP auth/ping endpoint (code + appsettings wiring).
- Reuse existing server auth backend/registry logic as much as possible.
- Deterministic test-vector generator for:
  - TCP auth message crypto pieces (RSA-OAEP + AES-GCM blobs)
  - UDP plane envelope for at least 1–2 known packets (see section 6)
- Minimal docs/comments near code describing the frozen wire protocol.

### B) Android-side
- New Android app project that produces an APK:
  - VpnService + TUN (MTU hardcoded to 1300)
  - UDP tunnel sender/receiver using existing UDP data plane format
  - Network switching support (Wi-Fi ↔ LTE, etc.)
  - Manual config import (simplest)
  - Minimal UI: Start/Stop + status color + minimal stats

## 1) Android TCP auth/ping protocol (FROZEN)

### 1.1 Transport framing
- Plain TCP socket.
- Messages are **length-prefixed**:
  - 4-byte unsigned length, **big-endian** (network order)
  - length excludes the 4 bytes
  - payload is UTF-8 JSON
- On read: must handle partial frames and buffering.

### 1.2 Crypto choices (industry-aligned)
- **RSA encryption:** RSA-OAEP with:
  - main digest: **SHA-256**
  - MGF1 digest: **SHA-1** (explicitly frozen for Android-provider compatibility)
- **Symmetric encryption:** **AES-256-GCM**
  - 12-byte IV/nonce
  - 16-byte tag
- **Encoding:** Base64 (standard) for binary fields.

### 1.3 Provisioned keys / identity (manual)
Android is provisioned out-of-band with:
- `clientId` (string)
- server endpoint (IP / DNS name)
- TCP auth port, UDP port
- server RSA public key (PEM or DER)
- whatever UDP plane key material the existing Windows client already uses (same format/meaning)

Server already has:
- RSA private key (or can be added in appsettings / file)
- existing client registry / allowed clients store

### 1.4 Message sequence
1) `client_hello`  →
2) ← `server_hello`
3) `auth_request`  →
4) ← `auth_response`
5) periodic `ping` ↔ `pong` while connected (optional but recommended)
6) if ping fails / network changes: Android reconnects and re-authenticates

### 1.5 JSON message schemas

All messages include:
- `type` : string
- `requestId` : string
- `tsUtc` : string (ISO-8601 UTC)

#### 1) ClientHello (client → server)
- `type = "client_hello"`
- `clientId` : string
- `clientNonceB64` : base64(32 random bytes)
- `clientVersion` : string (e.g., "android-0.1")
- `capabilities` : object (optional)

#### 2) ServerHello (server → client)
- `type = "server_hello"`
- `serverNonceB64` : base64(32 random bytes)
- `serverTimeUtc` : string
- `serverKeyId` : string (for future rotation; can be "default" now)

#### 3) AuthRequest (client → server)
- `type = "auth_request"`
- `clientId`
- `clientNonceB64`
- `serverNonceB64`
- `encryptedBlobB64` : base64(RSA-OAEP encrypt of UTF-8 JSON blob)
  Blob JSON fields:
  - `kClientB64` : base64(32 random bytes)  // client-chosen secret seed
  - `deviceInfo` : object (optional; minimal)
  - `wantUdp` : true
  - `wantMtu` : 1300

#### 4) AuthResponse (server → client)
- `type = "auth_response"`
- `ok` : bool
- if `ok = true`:
  - `sessionId` : string (GUID-like)
  - `udpEndpoint` : string (e.g. "1.2.3.4:40045")
  - `encryptedSessionB64` : base64(AES-256-GCM encrypt of UTF-8 JSON blob)
    - AES key derivation:
      - `kSession = HKDF-SHA256(ikm = kClient || clientNonce || serverNonce, salt = "vpn-auth", info = "session")`
      - output 32 bytes
    - Blob JSON fields:
      - `sessionAesKeyB64` : base64(32 bytes)  // key used by UDP plane if applicable
      - `sessionExpiresUtc` : string (optional; can be long)
      - `udpKeepaliveSec` : int (e.g. 15)
      - `serverAssignedIp` : string (optional, if your system assigns)
- if `ok = false`:
  - `errorCode` : string
  - `errorMessage` : string

#### 5) Ping
- `type = "ping"`
- `sessionId`
- `seq` : int

#### 6) Pong
- `type = "pong"`
- `sessionId`
- `seq`
- `serverTimeUtc`

### 1.6 Server acceptance rules
- Verify `clientId` is allowed (reuse existing registry).
- Verify nonces are fresh per connection (prevent replay).
- Create/refresh session mapping:
  - sessionId ↔ clientId ↔ UDP plane parameters (as already exists)
- Make ping cheap; allow it to extend session liveness.

## 2) Server implementation requirements

### 2.1 Where to implement
Implement TCP auth/ping listener inside the existing server solution near the VPN server entrypoint identified in report 045.
Prefer:
- A small TCP listener/service class with cancellation + logging
- Uses existing auth/registry types and emits the same session artifacts the UDP plane expects

### 2.2 Configuration (appsettings)
Add settings (names can be adjusted to match conventions; keep them in one section):
- `AndroidAuthTcpListenIp` (default 0.0.0.0)
- `AndroidAuthTcpPort` (default: existing WCF port + 1, but configurable)
- `UdpPort` (existing)
- `ServerPublicIpOrName` (if needed for `udpEndpoint`)
- RSA private key location or embedded key material reference

### 2.3 Logging
- Log connection accepted/closed
- Log auth success/failure (no secrets)
- Log sessionId creation/refresh
- Log pings rate-limited (avoid spam)

## 3) Android client implementation requirements

### 3.1 Project choice
Use Kotlin/Android SDK (recommended for fastest reliable APK), unless the repo already has a .NET Android strategy.
If you choose .NET Android, justify with repo evidence and keep it minimal.

### 3.2 VpnService behavior
- Provide a VpnService that:
  - establishes TUN with MTU=1300
  - sets routes for full tunnel (or follow existing split-tunnel policy if present; document choice)
  - sets DNS servers if needed (optional for initial working version)
- Must ensure the UDP/TCP control sockets bypass the VPN (protect/bind to underlying network).

### 3.3 Tunnel loops
- Loop A: read from TUN → wrap/encrypt (existing UDP plane) → send UDP to server
- Loop B: receive UDP from server → unwrap/decrypt → write to TUN
- Keepalive: send small UDP keepalive at configured interval (from auth response or default 15s)

### 3.4 Network switching
- On connectivity change:
  - stop UDP receive/send loops safely
  - re-bind/protect sockets for new network
  - re-run TCP auth (new session if needed)
  - resume UDP plane

### 3.5 Minimal UI (mandatory)
Create a simple Activity with:
- A single big Start/Stop button:
  - states: **Disconnected**, **Connecting**, **Connected**
  - colors:
    - Disconnected: red
    - Connecting: yellow/orange
    - Connected: green
- Minimal stats panel (text):
  - Server: `<ip-or-name>`
  - TCP auth port, UDP port
  - SessionId (shortened)
  - Bytes sent/received (UDP total)
  - Packets sent/received (optional)
  - Current transport network (Wi-Fi/LTE) if easily available
- UI must reflect real state transitions (not just optimistic).

## 4) Manual config + keys provisioning (simplest)

### 4.1 Format
Use a single config file imported via file picker (or copied via ADB) containing:
- server host/ip
- tcp auth port
- udp port
- clientId
- server RSA public key
- existing UDP plane key materials

Recommend JSON for simplicity.

### 4.2 Storage
On import:
- Store config in app-private storage.
- Keystore is out of scope for this stage.

### 4.3 Editing
Provide a basic “Settings” screen:
- shows current config values (excluding secrets or showing masked)
- allows re-import / overwrite config

## 5) Compatibility notes (must follow)
- Keep MTU=1300 (hardcoded).
- Do not change UDP message formats.
- Crypto must match: RSA-OAEP(SHA-256, MGF1-SHA1) and AES-256-GCM.

## 6) Test vectors (required)

### 6.1 Auth vectors
Server-side generator must output deterministically:
- Example ClientHello/ServerHello JSON
- Example RSA-OAEP encrypted blob (base64) for a known plaintext blob
- Example derived `kSession` (base64)
- Example AES-GCM encryptedSessionB64 for a known plaintext session blob (include IV/tag conventions)

Check vectors into repo under a predictable folder (e.g., `test_vectors/`) with a short README.

### 6.2 UDP vectors
Generate at least 2 vectors:
- One small IPv4 UDP packet payload
- One larger payload near MTU constraints

For each:
- Input: plaintext IP packet bytes (base64)
- Output: expected UDP datagram bytes sent to server (base64)
- And inverse for receive direction if applicable

Purpose: validate Android encode/decode without running full VPN.

## 7) Acceptance criteria

### Server
- TCP auth endpoint accepts Android client, returns sessionId + session material.
- Ping/pong works and updates liveness.
- No changes to existing Windows flows.

### Android
- APK installs and runs.
- User can import config.
- User can Start VPN and see Connecting → Connected.
- Tunnel passes traffic in emulator/BlueStacks (basic browsing + DNS).
- Stats update (bytes in/out at minimum).
- Handles switching networks (toggle Wi-Fi off/on) without app restart.

---

End of spec.
